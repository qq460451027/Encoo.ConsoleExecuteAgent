using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Encoo.ConsoleExcuteAgent;
namespace ConsoleExcuteAgent
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        public string dirStorage;
        public string fileRunInstanceList;
        public EncooHTTPHelper helper;
        public List<WorkflowListItem> workflowList;
        public DataTable loadedCurrentWorkflowParamTable;
        public DataTable runInstanceIDTable;
        public string fileLog = "";
        #region 窗口相关函数
        private void frmMain_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists("Logs")) {
                    Directory.CreateDirectory("Logs");
                }
                fileLog = @"Logs\" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
                runInstanceIDTable = new DataTable();
                runInstanceIDTable.Columns.Add("流程名");
                runInstanceIDTable.Columns.Add("实例ID");
                runInstanceIDTable.Columns.Add("实例状态");
                runInstanceIDTable.Columns.Add("最后更新时间");
                dgWorkflowStatus.ItemsSource = runInstanceIDTable.DefaultView;
                string URL = ConfigurationManager.AppSettings["consoleUrl"];
                string Username = ConfigurationManager.AppSettings["userName"];
                string Password = Encoding.UTF8.GetString( Convert.FromBase64String(ConfigurationManager.AppSettings["password"]));
                string companyId = ConfigurationManager.AppSettings["companyId"];
                string departmentId = ConfigurationManager.AppSettings["departmentId"];
                setStatus(Username + "|" + Password);
                chkRecord.IsChecked = true;
                Task.Run(() => { 
                    helper = new EncooHTTPHelper(Username,Password,URL);
                    helper.logEvent += setStatus;
                    helper.Initialization();
                    helper.setCurrentDepartmentID(departmentId);
                    workflowList = helper.getWorkflowsList();
                    loadWorkflowList();
                    startSyncStatus();           
                });
            }
            catch (Exception ex)
            {
                setStatus("初始化遇到错误，"+ex.Message);
                throw;
            }
        }
        private void checkLogfile() {
            fileLog = @"Logs\" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
            if (!File.Exists(fileLog)) {
                File.AppendAllText(fileLog,"Start Logging...");
            }
        }
        private void setStatus(string text)
        {
            Dispatcher.Invoke(() => {
                try
                {
                    statusText.Text = text; 
                    File.AppendAllLines(fileLog, new string[] { $"{DateTime.Now.ToString()} {text}" });
                }
                catch (Exception)
                {
                    throw;
                }

            });

        }

        private void combPackageNameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var x = (ComboBox)sender;
            loadWorkflowParam(x.SelectedValue.ToString());
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {

        }

        private void UITextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var o = (TextBox)sender;
            o.Focus();
            o.SelectAll();
        }
        #endregion 窗口相关函数

        /// <summary>
        /// 加载流程部署名到comboBox下拉框
        /// </summary>
        public void loadWorkflowList() {
            Dispatcher.Invoke(() => {
                combPackageNameList.Items.Clear();
                foreach (var v in workflowList)
                {
                    combPackageNameList.Items.Add(v.name);
                }
                if (combPackageNameList.Items.Count == 1)
                {
                    combPackageNameList.SelectedIndex = 0;
                }
            });
        }
        /// <summary>
        /// 加载流程部署的参数到参数列表中
        /// </summary>
        /// <param name="workflowName"></param>
        public void loadWorkflowParam(string workflowName)
        {
            checkLogfile();
            try
            {
                loadedCurrentWorkflowParamTable = new DataTable();
                //helper.getWorkflowParams(workflowName);
                loadedCurrentWorkflowParamTable.Columns.Add("属性名");
                loadedCurrentWorkflowParamTable.Columns.Add("属性值");
                var t = workflowList.Where(a => a.name== workflowName);
                WorkflowListItem wi = t.ToList()[0];
                List<ArgumentsItem> args = wi.arguments;
                //debug info
                foreach (var x in args)
                {
                    if (x.direction.ToLower() == "in".ToLower())
                    {
                        loadedCurrentWorkflowParamTable.Rows.Add(new string[] { x.name, x.defaultValue });
                    }
                }
                Dispatcher.Invoke(() =>
                {
                    if (string.IsNullOrEmpty(wi.description))
                    {
                        labelParamsOrMemo.Content = "参数表";
                        labelParamsOrMemo.Foreground = new SolidColorBrush(Colors.Black);
                    }
                    else
                    {
                        labelParamsOrMemo.Content = wi.description;
                        labelParamsOrMemo.Foreground = new SolidColorBrush(Colors.Red);
                    }
                    dgPackageParams.ItemsSource = loadedCurrentWorkflowParamTable.DefaultView;
                });
            }
            catch (Exception ex)
            {
                setStatus("加载参数异常，"+ex.Message);
            }

        }
        /// <summary>
        /// 确认参数是最新的，如有未同步的则修改
        /// </summary>
        /// <param name="item"></param>
        private void syncParam(WorkflowListItem item) {
            string cache = "";
            try
            {
                foreach (DataRow dr in loadedCurrentWorkflowParamTable.Rows) {
                    foreach (var x in item.arguments) {
                        if (x.name == dr["属性名"].ToString() && x.defaultValue != dr["属性值"].ToString()) {
                            x.defaultValue = dr["属性值"].ToString();
                            setStatus($"已将{x.name} {x.defaultValue}更新为{dr["属性值"].ToString()}");
                            break;
                        }
                    } 
                }
            }
            catch (Exception exx)
            {
                setStatus("同步参数异常，"+exx.Message);
            }
            
        }
        /// <summary>
        /// 同步本次程序运行后的所有实例的状态
        /// </summary>
        private void startSyncStatus() {
            Task.Run(() => {
                while (true) {
                    checkLogfile();
                    try
                    {
                        //DataRow[] drs = runInstanceIDTable.Select($"实例状态 <> 'Success'");
                        if (runInstanceIDTable != null && runInstanceIDTable.Rows.Count > 0)
                        {
                            foreach (DataRow dr in runInstanceIDTable.Rows)
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(dr["实例状态"].ToString())) {
                                        if (!dr["实例状态"].ToString().ToLower().StartsWith("succ")) { 
                                            setStatus($"开始查询{dr["实例ID"]}的状态");
                                            string currentStatus = helper.getRunInstanceLogDetails(dr["实例ID"].ToString()).status;
                                            if (!string.IsNullOrEmpty(currentStatus)){
                                                dr["实例状态"] = currentStatus;
                                            }
                                            dr["最后更新时间"] = DateTime.Now.ToString();                                           
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    dr["实例状态"] = "更新异常," + ex.Message;
                                    dr["最后更新时间"] = DateTime.Now.ToString();
                                    setStatus("遇到异常，" + ex.Message);
                                }
                            }
                        }
                    }
                    catch (Exception ex1)
                    {
                        setStatus(ex1.Message);
                    }
                    finally {
                        setStatus("准备更新状态表 " + DateTime.Now.ToString());
                        Dispatcher.Invoke(() => { 
                            dgWorkflowStatus.ItemsSource = runInstanceIDTable.DefaultView;
                        });
                        setStatus("已更新状态表 "+DateTime.Now.ToString());
                    }
                    Thread.Sleep(5000);
                }
            });
        }
        private void btnExcute_Click(object sender, RoutedEventArgs e)
        {
            bool recordCheck = chkRecord.IsChecked.Value;
            if (combPackageNameList.SelectedValue == null || string.IsNullOrEmpty(combPackageNameList.SelectedValue.ToString())) { 
                MessageBox.Show("请先选择要执行的流程名");
                return;
            }
            string workflowName = combPackageNameList.SelectedValue.ToString();
            try
            {
                var currentWorkflow = workflowList.Where(a => a.name == workflowName).ToList();
                if (currentWorkflow.Count == 1) {
                    syncParam(currentWorkflow[0]);
                    workflowExecuteParams workflowExecuteParams = new workflowExecuteParams();
                    if (recordCheck)
                        workflowExecuteParams.videoRecordMode = VideoRecordMode.AlwaysRecord.GetHashCode().ToString();
                    workflowExecuteParams.arguments = currentWorkflow[0].arguments;
                    RunInstanceLogItem d = helper.executeJob(currentWorkflow[0].id,workflowExecuteParams);
                    //DataRow[] drs = runInstanceIDTable.Select($"流程名='{workflowName}'and 实例ID='{d.lastRunInstaceId}'");
                    //if (drs==null||drs.Length<1) {
                        runInstanceIDTable.Rows.Add(new string[] {workflowName,d.lastRunInstaceId,"已提交运行",DateTime.Now.ToString()});
                        setStatus($"ID：{d.lastRunInstaceId}已添加到实例列表");
                    //}
                    setStatus("已发送请求    本流程实例ID："+d.lastRunInstaceId+"|最后触发时间"+DateTime.Parse(d.createdAt.ToString()).ToString());
                    MessageBox.Show($"流程已运行   流程名：{workflowName}\r\n实例ID:{d.lastRunInstaceId}\r\n处理机器人：{d.lastRobotName}\r\n当前状态：{d.lastState}\r\n","流程触发通知",MessageBoxButton.OK,MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                setStatus("尝试执行流程异常，"+ex.Message);
                throw;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            
        }

        private void menuOpenLog_Click(object sender, RoutedEventArgs e)
        {

            if (File.Exists(fileLog))
            {
                Process.Start(fileLog);
            }
            else {
                setStatus("未找到日志文件："+fileLog);
            }
        }

        private void menuOpenConfig_Click(object sender, RoutedEventArgs e)
        {

            string confFile = "ConsoleExcuteAgent.exe.config";
            if (File.Exists(confFile)) {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "notepad.exe";
                psi.Arguments = Path.Combine(Environment.CurrentDirectory,confFile);
                Process.Start(psi);
            }
        }

        private void menuOpenUpdatelog_Click(object sender, RoutedEventArgs e)
        {
            string confFile = "使用、更新说明.txt";
            if (File.Exists(confFile))
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "notepad.exe";
                psi.Arguments = Path.Combine(Environment.CurrentDirectory, confFile);
                Process.Start(psi);
            }
        }

        private void menuLogFeedback_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string from = ConfigurationManager.AppSettings["mailUsername"];
                string to = ConfigurationManager.AppSettings["mailFeedbackTo"];
                
                string exchangefile = new FileInfo( Path.Combine("Logs", "Feedback" + DateTime.Now.ToString("yyyyMMddHHmmss")+".log")).FullName;
                //暂时写死，不放配置里
                using (SmtpClient client=new SmtpClient()) {
                    client.Host = ConfigurationManager.AppSettings["mailHost"];
                    client.Port = 25;
                    client.DeliveryMethod=SmtpDeliveryMethod.Network;
                    client.Credentials = new NetworkCredential(from, ConfigurationManager.AppSettings["mailPassword"]);
                    client.EnableSsl = true;
                    MailMessage mail = new MailMessage(from, to);
                    mail.Subject = "流程部署执行代理异常反馈";
                    mail.Sender =new MailAddress(from);
                    mail.Body = "具体内容见附件日志。" + DateTime.Now.ToString();
                    File.Copy(fileLog,exchangefile);
                    mail.Attachments.Add(new Attachment(exchangefile));
                    client.Send(mail);
                    MessageBox.Show("发送成功", "发送反馈成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    setStatus("发送成功");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送反馈邮件异常，"+ex.Message,"发送邮件异常",MessageBoxButton.OK,MessageBoxImage.Warning);
                setStatus("发送反馈邮件异常，" + ex.Message);
            }
        }
    }
}
