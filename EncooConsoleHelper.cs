using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
namespace Encoo.ConsoleExcuteAgent
{
    public class EncooHTTPHelper : IDisposable
    {
        //备用，调试随机数生成
        Random r = new Random();
        //内部变量
        private string _baseURL;
        private string _SSO;
        private string _ApiGateway;
        private string _userName;
        private string _password;
        private string _Bearer;
        private string _refreshToken;
        private DateTime _accessTokenExpireDate;
        private int pageSize = 20;
        // 以下为header中的三参数
        private string _accessToken;
        private string _companyID;
        private string _currentDepartmentID;
        private Task _tokenAutoRefreshTask;
        //缓存的封装类，备用
        private CompanyListWrapper _companyListWrapper;
        private DepartmentTreeWrapper _departmentTreeWrapper;
        #region URL
        //URL清单暂未写入配置文件
        public string URL_getTaskList = "/v2/jobs";
        public string URL_getInstanceLog = "/v2/RunInstances/{id}/logs";
        public string URL_getCompanyList = "/v2/companies/companylist";
        public string URL_getDepartmentTree = "/v2/departments/tree";
        public string URL_getSubDepartmentList = "/v2/departments/{id}/children";
        public string URL_execute = "/v2/workflows/{id}/execute";
        public string URL_getWorkflowInfo = "/v2/workflows/{id}";
        public string URL_getRunInstanceDetails = "/v2/runinstances/{id}/result";
        #endregion URL
        #region 构造函数
        /// <summary>
        /// 默认构造函数
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="passWord"></param>
        /// <param name="baseURL">http://xxx.xxx.com</param>
        public EncooHTTPHelper(string userName, string passWord, string baseURL)
        {
            _SSO = (baseURL.EndsWith("/")?baseURL.Substring(0,baseURL.Length-1):baseURL )+ ":81";
            _ApiGateway = (baseURL.EndsWith("/")?baseURL.Substring(0, baseURL.Length - 1):baseURL )+ ":8080";
            _userName = userName;
            _password = passWord;
            _accessTokenExpireDate = DateTime.Now.AddSeconds(300);
            Initialization();
        }
        /// <summary>
        /// 自定义构造函数
        /// </summary>
        /// <param name="SSO">http://xxx.xxx.com:81</param>
        /// <param name="APIGateway">http://xxx.xxx.com:8080</param>
        public EncooHTTPHelper(string SSO, string APIGateway, string userName, string passWord)
        {
            _SSO = (SSO.EndsWith("/")?SSO.Substring(0,SSO.Length-1):SSO) + ":81";
            _ApiGateway = (APIGateway.EndsWith("/")?APIGateway.Substring(0,APIGateway.Length-1):APIGateway )+ ":8080";
            _userName = userName;
            _password = passWord;
            _accessTokenExpireDate = DateTime.Now.AddSeconds(300);
            Initialization();
        }
        /// <summary>
        /// 初始化所有必要参数，如AuthCode,CompanyID等
        /// </summary>
        public void Initialization()
        {
            _accessTokenExpireDate = DateTime.Now.AddDays(365);
            print("开始初始化。开始获取令牌");
            getAuth();
            print("完成获取令牌，开始获取公司ID");
            cacheCompanyID();
            print("完成获取公司ID，就开始获取部门树");
            cacheDepartmentTree();
            print("完成获取部门树，结束初始化");
            startAutoRefreshTokenTask();
            print("已开始自动刷新Token线程");
;        }

        public void Dispose()
        {
            _companyListWrapper = null;
            GC.Collect();
        }
        #endregion 构造函数
        #region 鉴权
        /// <summary>
        /// 输出accessToken(展示，非获取)k
        /// </summary>
        /// <returns></returns>
        public string printAccessToken()
        {
            print(_accessToken);
            return _accessToken;
        }
        /// <summary>
        /// 获取accessToken,存入_accessToken
        /// </summary>
        private void getAuth()
        {
            try
            {
                Dictionary<string, string> contentList = new Dictionary<string, string>();
                contentList.Add("client_id", "thirdpartyservice");
                contentList.Add("grant_type", "password");
                contentList.Add("username", _userName);
                contentList.Add("password", _password);
                string json = Post(_SSO + "/connect/token", contentList);
                if (json.Contains("error"))
                {
                    throw new Exception("获取令牌失败，错误：" + json);
                }
                else { 
                    AuthClass auth = (AuthClass)JsonConvert.DeserializeObject(json, typeof(AuthClass));
                    _accessToken = auth.access_token;
                    _accessTokenExpireDate = DateTime.Now.AddSeconds(auth.expires_in);
                    _Bearer = "Bearer " + _accessToken;
                    _refreshToken = auth.refresh_token;
                    print($"获取到accessToken:{_accessToken} 过期时间:{_accessTokenExpireDate} 有效期:{auth.expires_in}");
                }
               
            }
            catch (Exception ex)
            {
                print("获取令牌异常，"+ex.Message);
                throw;
            }
        }
        /// <summary>
        /// 刷新过期令牌
        /// </summary>
        private void refreshAuth()
        {
            //if (true)
            if (_accessTokenExpireDate <= DateTime.Now)
            {
                print($"检测到token有效期已超过当前时间，准备更新  当前时间{DateTime.Now} token过期时间{_accessTokenExpireDate}");
                //_accessTokenExpireDate = DateTime.Now.AddMinutes(1);
                string retJson = "";
                try
                {
                    Dictionary<string, string> contentList = new Dictionary<string, string>();
                    contentList.Add("client_id", "thirdpartyservice");
                    contentList.Add("grant_type", "refresh_token");
                    contentList.Add("refresh_token",_refreshToken);
                    print($"刷新传递的参数  refresh_token【{contentList["refresh_token"]}】");
                    using (HttpClient http = new HttpClient())
                    {
                        HttpContent hc = new FormUrlEncodedContent(contentList);
                        retJson = http.PostAsync(_SSO + "/connect/token", hc).Result.Content.ReadAsStringAsync().Result;
                        print("刷新token收到json:"+retJson);
                        try
                        {
                            refreshAuthClass refreshAuth = (refreshAuthClass)JsonConvert.DeserializeObject(retJson, typeof(refreshAuthClass));
                            if (_accessToken != refreshAuth.access_token)
                            {
                                //print($"将更新accessToken 原始token【{_accessToken}】 新token【{refreshAuth.access_token}】");
                                _accessToken = refreshAuth.access_token;
                                _Bearer = "Bearer " + _accessToken;
                            }
                            else {
                                print("token无变化");
                            }
                            if (_refreshToken!=refreshAuth.refresh_token) { 
                                _refreshToken=refreshAuth.refresh_token;
                            }
                            _accessTokenExpireDate = DateTime.Now.AddSeconds(refreshAuth.expires_in);
                            if (refreshAuth.refresh_token!=_refreshToken) { 
                                _refreshToken=refreshAuth.refresh_token;
                            }
                            //getAuth();
                            print($"刷新后_accessToken 时限：{refreshAuth.expires_in} 有效期至：{_accessTokenExpireDate}");
                        }
                        catch (Exception ex)
                        {
                            print("刷新token过程反序列化可能异常,"+ex.Message);
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    print($"刷新令牌异常，JSON：{retJson}    异常：" + ex.Message);
                }
            }
            else { 
                //无需刷新
            }
        }
        /// <summary>
        /// 获取公司顶级(Headers备用)
        /// </summary>
        private void cacheCompanyID()
        {
            try
            {
                string obj = Get(_ApiGateway + URL_getCompanyList);
                _companyListWrapper = (CompanyListWrapper)JsonConvert.DeserializeObject(obj, typeof(CompanyListWrapper));
                _companyID = _companyListWrapper.companies[0].companyId;
                print("缓存到公司ID：" + _companyID);
            }
            catch (Exception ex)
            {
                print("缓存公司ID异常，" + ex.Message);
                throw;
            }

        }
        /// <summary>
        /// 缓存部门树信息
        /// </summary>
        private void cacheDepartmentTree()
        {
            refreshAuth();
            try
            {
                _departmentTreeWrapper = (DepartmentTreeWrapper)JsonConvert.DeserializeObject(Get(_ApiGateway + URL_getDepartmentTree), typeof(DepartmentTreeWrapper));
                _currentDepartmentID = _departmentTreeWrapper.rootDepartment.departmentId;
                print("缓存到部门ID:" + _currentDepartmentID);
            }
            catch (Exception ex)
            {
                print("缓存部门树DepartmentTree遇到错误" + ex.Message);
                throw;
            }
        }
        #endregion 鉴权
        /// <summary>
        /// 测试函数
        /// </summary>
        #region 部门
        /// <summary>
        /// 将请求的目标DepartmentID设定为指定部门的ID
        /// </summary>
        /// <param name="departmentName"></param>
        /// <param name="matchFullPath"></param>
        public bool setCurrentDepartmentID(string departmentID)
        {
            _currentDepartmentID = departmentID;
            return true;
        }
        /// <summary>
        /// 通过部门名称获取部门ID
        /// </summary>
        /// <param name="departmentName">部门名称</param>
        /// <param name="matchFullPath">指定部门通过全路径匹配或通过简称匹配</param>
        /// <returns></returns>
        public string getDepartmentIDFromDepartmentName(string departmentName, bool matchFullPath)
        {
            try
            {
                if (string.IsNullOrEmpty(departmentName))
                    throw new Exception("指定部门ID时未设定要匹配的部门名称");

                foreach (var item in _departmentTreeWrapper.rootDepartment.children)
                {
                    bool s = recusiveFindDepartmentIDandName(item, departmentName, matchFullPath, out string currentDepartmentID, out string currentDepartmentName);
                    if (s)
                    {
                        print($"部门{departmentName} departmentID为{currentDepartmentID}");
                        return currentDepartmentID;
                    }
                    else
                    {
                        continue;
                    }
                }
                return "";
            }
            catch (Exception ex)
            {
                print("通过部门名称获取部门ID遇到错误，" + ex.Message);
                throw;
            }
        }
        /// <summary>
        /// 递归获取缓存部门树中指定部门的部门id并设定到)_currentDepartmentID中
        /// </summary>
        /// <param name="cdi">传递children中的子部门容器类</param>
        /// <param name="name">需要查找的部门名称</param>
        /// <param name="matchFullPath">指定是否匹配部门全路径（或者匹配简称）</param>
        /// <returns></returns>
        private bool recusiveFindDepartmentIDandName(ChildrenDepartmentItem cdi, string name, bool matchFullPath, out string matchedDepartmentID, out string matchedDepartmentName)
        {
            matchedDepartmentID = "";
            try
            {
                //int identID = r.Next();
                if (cdi != null)
                {
                    //print($"{identID} 开始处理");
                    if (cdi.name == name)
                    {
                        print($"找到匹配的项{cdi.departmentPath} 目标部门{name}  ID:{cdi.departmentId}");
                        matchedDepartmentID = cdi.departmentId;
                        matchedDepartmentName = cdi.name;
                        return true;
                    }
                    else
                    {
                        print($"当前层级不符合   当前部门名为{cdi.name}  目标部门为{name} 开始尝试寻找本部门的下属部门");
                        if (cdi.children.Count <= 0)
                        {
                            if ((cdi.departmentPath == name && matchFullPath) || (cdi.name == name && !matchFullPath))
                            {
                                print($"{cdi.departmentPath}->{cdi.name} 无子集，找到匹配项{name},返回true");
                                matchedDepartmentID = null;
                                matchedDepartmentName = null;
                                return false;
                            }
                            else
                            {
                                print($"{cdi.departmentPath}->{cdi.name} 无子集，未找到匹配项{name},返回false");
                                matchedDepartmentID = null;
                                matchedDepartmentName = null;
                                return false;
                            }
                        }
                        else
                        {
                            //print($"{cdi.departmentPath} 有子集，开始处理子集");
                            foreach (var item in cdi.children)
                            {
                                //print($"{cdi.departmentPath} 开始处理{item.departmentPath}的children");
                                bool ret = recusiveFindDepartmentIDandName(item, name, matchFullPath, out matchedDepartmentID, out matchedDepartmentName);
                                //print($"{cdi.departmentPath} 完成处理{item.departmentPath}的children，结果{ret}");
                                if (!ret)
                                {
                                    continue;
                                }
                                else
                                {
                                    return ret;
                                }

                                //print($"{identID} 完成处理{item.name}的children");
                            }

                        }
                        matchedDepartmentID = null;
                        matchedDepartmentName = null;
                        return false;
                    }
                }
                else
                {
                    matchedDepartmentID = null;
                    matchedDepartmentName = null;
                    return false;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        /// <summary>
        /// 获取下一级子部门
        /// </summary>
        /// <param name="departmentID"></param>
        /// <returns></returns>
        private ChildrenDepartmentItem[] getChildremDepartmentObject(ChildrenDepartmentItem cdi, string departmentName, bool isShortName)
        {
            List<ChildrenDepartmentItem> list = new List<ChildrenDepartmentItem>();
            ///无下级退出
            if (cdi == null || cdi.children.Count == 0)
                return null;
            //有下级遍历
            foreach (ChildrenDepartmentItem item in _departmentTreeWrapper.rootDepartment.children)
            {
                bool b = recusiveFindDepartmentIDandName(item, departmentName, isShortName, out string currentID, out string currentName);
                if (b)
                {
                    if (string.IsNullOrEmpty(currentID) || string.IsNullOrEmpty(currentName))
                    {
                        return null;
                    }
                    else
                    {
                        //本地缓存内存在有效内容，从服务端获取id
                        //Post(_ApiGateway + URL_getSubDepartmentList);
                    }
                }
            }
            return null;
        }
        #endregion 部门
        #region 流程包
        /// <summary>
        /// 获取流程包信息
        /// </summary>
        /// <param name="packageName"></param>
        /// <returns></returns>
        public packageInfo getPackageDetails(string packageName)
        {
            try
            {

                return null;
            }
            catch (Exception)
            {

                throw;
            }
        }
        public allPackageResponseInfo getAllPackageDetailsInDepartment()
        {
            try
            {
                Dictionary<string, string> content = new Dictionary<string, string>();
                content.Add("name", "");
                content.Add("tags", "");
                content.Add("nameortag", "");
                content.Add("projectname", "");
                content.Add("pageindex", "");
                content.Add("pagesize", "");
                return null;
            }
            catch (Exception)
            {
                throw;
            }
        }
        #endregion 流程包
        #region 任务记录
        public JobDTOV2 getJobDTOV2(string packageOrWorkflowName)
        {
            HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, "");
            return (JobDTOV2)null;
        }
        /// <summary>
        /// 返回执行状态列表
        /// </summary>
        /// <param name="queryType"></param>
        /// <param name="startTime"></param>
        /// <param name="endTime"></param>
        /// <param name="isDesc"></param>
        /// <param name="pageSize"></param>
        /// <param name="pageIndex"></param>
        /// <returns></returns>
        public RunInstanceLogWrapper getRunInstanceLogList(EnumQueryTimeType queryType, DateTime startTime, DateTime endTime, bool isDesc, int pageSize, int pageIndex)
        {
            try
            {
            
                //string s = Get(_ApiGateway + URL_getTaskList);
                JobQueryURIArgs arg = new JobQueryURIArgs();
                arg.QueryTimeType = queryType;
                arg.StartQueryTime = startTime;
                arg.EndQueryTime = endTime;
                arg.IsDesc = isDesc;
                arg.PageSize = pageSize;
                arg.PageIndex = pageIndex;
                string url = arg.getURLArgs();
                string jobLogJson = Get(url);
                return (RunInstanceLogWrapper)JsonConvert.DeserializeObject(jobLogJson, typeof(RunInstanceLogWrapper));
            }
            catch (Exception ex)
            {
                print("获取流程运行日志异常，"+ex.Message);
                return null;
            }

        }

        public runInstanceLogDetailsSimpleResult getRunInstanceLogDetails(string runInstanceID) {
            string requestUrl = _ApiGateway + URL_getRunInstanceDetails.Replace("{id}", runInstanceID);
            string jsonRes = Get(requestUrl);
            print("获取日志简单结果请求："+jsonRes);
            try
            {
                return (runInstanceLogDetailsSimpleResult)JsonConvert.DeserializeObject(jsonRes,typeof(runInstanceLogDetailsSimpleResult));
            }
            catch (Exception ex)
            {
                print("获取日志简单结果异常，"+ex.Message);
                return null;
            }

        }
        #endregion 任务记录
        #region 流程部署

        public List<WorkflowListItem> getWorkflowsList()
        {
            List<WorkflowListItem> retObj = new List<WorkflowListItem>();
            string requestURL = _ApiGateway + URL_getWorkflowInfo.Replace("/{id}", "?pageIndex=0&pageSize=200&isDesc=false");
            string resultJson = Get(requestURL);
            print("获取到流程部署清单json:" + resultJson);
            WorkflowListWrapper wflw = (WorkflowListWrapper)JsonConvert.DeserializeObject(resultJson, typeof(WorkflowListWrapper));
            return wflw.list;
        }
        public List<ArgumentsItem> getWorkflowParams(string packageId)
        {
            string requestURL = _ApiGateway + URL_getWorkflowInfo.Replace("{id}", packageId);
            string resultJson = Get(requestURL);
            print("获取的流程部署JSON:" + resultJson);
            WorkflowListItem pi = (WorkflowListItem)JsonConvert.DeserializeObject(resultJson, typeof(WorkflowListItem));
            return pi.arguments;
        }
        /// <summary>
        /// 从提供的流程部署列表中获取指定流程的参数列表
        /// </summary>
        /// <param name="packageId"></param>
        /// <param name="workflowListItems"></param>
        /// <returns></returns>
        public List<ArgumentsItem> getWorkflowParams(string packageId, List<WorkflowListItem> workflowListItems)
        {
            try
            {
                foreach (var x in workflowListItems)
                {
                    if (x.id == packageId)
                        return x.arguments;
                    else
                        continue;
                }
                return null;
            }
            catch (Exception ex)
            {
                print("获取流程包参数失败，"+ex.Message);
                throw;
            }

        }
        /// <summary>
        /// 获取流程部署详细情况(未完成)
        /// </summary>
        /// <returns></returns>
        public string getWorkflowDetails(string workflowId) {
            try
            {
                string requestUrl = _ApiGateway + URL_getWorkflowInfo;
                string resultJson = Get(requestUrl);
                print("流程部署详细json:"+resultJson);

                return resultJson;
            }
            catch (Exception)
            {

                throw;
            }
        }
        /// <summary>
        /// 执行流程部署
        /// </summary>
        /// <param name="workflowId"></param>
        /// <param name="executeParam"></param>
        /// <returns></returns>
        public RunInstanceLogItem executeJob(string workflowId, workflowExecuteParams executeParam)
        {
            //
            string requestURL = _ApiGateway + URL_execute.Replace("{id}", workflowId);
            string jsonSent = JsonConvert.SerializeObject(executeParam);
            print("生成的execute发送json: " + jsonSent);
            string t = Post(requestURL, jsonSent);
            print("接收到的execute结果json: " + t);
            List<RunInstanceLogItem> executeRes = (List<RunInstanceLogItem>)JsonConvert.DeserializeObject(t, typeof(List<RunInstanceLogItem>));
            print(executeRes == null ? "null" : executeRes[0].name);
            return executeRes[0];
        }
        
        
        #endregion 流程部署
        /// <summary>
        /// 日志详细内容倒置并转换成列表
        /// </summary>
        /// <param name="objectresult"></param>
        /// <returns></returns>
        public List<string> ConvertLogObjectToList(QueryLogResult objectresult)
        {
            try
            {
                Stack<string> stack = new Stack<string>();
                foreach (RunInstanceLogEntryDTO item in objectresult.data)
                {
                    stack.Push(item.message);
                }
                return stack.ToList();
            }
            catch (Exception)
            {
                throw;
            }
        }

        #region GET\POST等基础功能
        /// <summary>
        /// 提交POST请求（表单形式application/x-www-form-urlencoded）
        /// </summary>
        /// <param name="uri">目标URIne</param>
        /// <param name="formValuesDict">表单键值对</param>
        /// <returns></returns>
        public string Post(string uri, Dictionary<string, string> formValuesDict)
        {
            print("开始Get获取" + uri);
            using (HttpClient hc = new HttpClient())
            {
                try
                {
                    HttpRequestMessage request = new HttpRequestMessage();
                    request.Method = HttpMethod.Post;
                    request.RequestUri = new Uri(uri);
                    request.Content = new FormUrlEncodedContent(formValuesDict);
                    fillHeaders(hc);
                    HttpResponseMessage res = hc.SendAsync(request).Result;
                    print("获取到" + res.Content.ReadAsStringAsync().Result);
                    return res.Content.ReadAsStringAsync().Result;
                    //if (res.StatusCode == HttpStatusCode.OK)
                    //{
                    //    return "Post正常"
                    //}
                    //else {
                    //    return "Post异常";
                    //}
                }
                catch (Exception ex)
                {
                    print(ex.Message);
                    throw;
                }
            }
        }
        /// <summary>
        /// Post提交字符串
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="JsonContent"></param>
        /// <returns></returns>
        public string Post(string uri, string JsonContent)
        {
            print($"开始Post提交 地址【{uri}】内容【{JsonContent}】");
            using (HttpClient hc = new HttpClient())
            {
                try
                {
                    HttpRequestMessage request = new HttpRequestMessage();
                    request.Method = HttpMethod.Post;
                    request.RequestUri = new Uri(uri);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Content = new StringContent(JsonContent, Encoding.UTF8, "application/json");
                    fillHeaders(hc);
                    HttpResponseMessage res = hc.SendAsync(request).Result;
                    print("获取到" + res.Content.ReadAsStringAsync().Result);
                    return res.Content.ReadAsStringAsync().Result;
                    //if (res.StatusCode == HttpStatusCode.OK)
                    //{
                    //    return "Post正常"
                    //}
                    //else {
                    //    return "Post异常";
                    //}
                }
                catch (Exception ex)
                {
                    print(ex.Message);
                    throw;
                }
            }
        }
        /// <summary>
        /// 提交Get请求
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        public string Get(string uri)
        {
            //print("开始Get获取"+uri);
            using (HttpClient hc = new HttpClient())
            {
                try
                {
                    HttpRequestMessage request = new HttpRequestMessage();
                    request.Method = HttpMethod.Get;
                    request.RequestUri = new Uri(uri);
                    fillHeaders(hc);
                    HttpResponseMessage res = hc.SendAsync(request).Result;
                    //print("获取到"+res.Content.ReadAsStringAsync().Result);
                    return res.Content.ReadAsStringAsync().Result;
                }
                catch (Exception ex)
                {
                    print(ex.Message);
                    throw;
                }
            }
        }
        public string Get(string uri, Dictionary<string, string> formValuesDict)
        {
            //print("开始Get获取" + uri);
            using (HttpClient hc = new HttpClient())
            {
                try
                {
                    HttpRequestMessage request = new HttpRequestMessage();
                    request.Method = HttpMethod.Get;
                    request.RequestUri = new Uri(uri);
                    request.Content = new FormUrlEncodedContent(formValuesDict);
                    fillHeaders(hc);
                    HttpResponseMessage res = hc.SendAsync(request).Result;
                    //print("获取到" + res.Content.ReadAsStringAsync().Result);
                    return res.Content.ReadAsStringAsync().Result;
                }
                catch (Exception ex)
                {
                    print(ex.Message);
                    throw;
                }
            }
        }
        private void startAutoRefreshTokenTask() {
            if (_tokenAutoRefreshTask==null||_tokenAutoRefreshTask.Status!=TaskStatus.Running) { 
                _tokenAutoRefreshTask = new Task(
                () => {
                    while (true) {
                        try
                        {
                            if (DateTime.Now>=_accessTokenExpireDate) {
                                refreshAuth();
                                print($"令牌已过期，刷新令牌 有效期更新为{_accessTokenExpireDate}");
                            }
                        }
                        catch (Exception)
                        {

                            throw;
                        }
                        finally {
                            Thread.Sleep(1000);
                        }
                    }
                }    
                );
                _tokenAutoRefreshTask.Start();
            }
            
        }
        /// <summary>
        /// 填充头HttpRequestMessage
        /// </summary>
        /// <param name="hrm">HttpRequestMessage</param>
        private void fillHeaders(HttpClient hrm)
        {
            //print("开始填充headers");
            try
            {
                
                if (!string.IsNullOrEmpty(_accessToken))
                {
                    hrm.DefaultRequestHeaders.Add("Authorization", _Bearer);
                }
                if (!string.IsNullOrEmpty(_companyID))
                    hrm.DefaultRequestHeaders.Add("CompanyId", _companyID);
                if (!string.IsNullOrEmpty(_currentDepartmentID))
                    hrm.DefaultRequestHeaders.Add("DepartmentId", _currentDepartmentID);
                //print("填充headers完成");
            }
            catch (Exception)
            {

                throw;
            }
        }
        /// <summary>
        /// 日志事件
        /// </summary>
        /// <param name="text"></param>
        private void print(string text)
        {
            try
            {
                if (logEvent != null) { 
                    logEvent.Invoke($"{DateTime.Now.ToString()} {text}");                
                }
            }
            catch (Exception)
            {

            }

        }
        private class httpErrorCode
        {

            /// <summary>
            /// 
            /// </summary>
            public string type { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string title { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public int status { get; set; }
            /// <summary>
            /// 
            /// </summary>
            public string traceId { get; set; }

        }
        /// <summary>
        /// 对外日志发布
        /// </summary>
        public event logEventDel logEvent;
        public delegate void logEventDel(string text);
        #endregion  GET\POST等基础功能
    }
    #region 容器类
    /// <summary>
    /// 流程执行返回结果
    /// </summary>
    public class WorkflowExecuteMsg
    {
        public string workflowId;
        public string robotId;
        public string errorMessage;
    }
    /// <summary>
    /// 流程部署列表 外层容器
    /// </summary>
    public class WorkflowListWrapper
    {
        public int count;
        public List<WorkflowListItem> list;
    }
    public class WorkflowListItem : WorkflowDTO
    {
    }
    /// <summary>
    /// 流程执行参数类
    /// </summary>
    [Serializable]
    public class workflowExecuteParams
    {
        public List<ArgumentsItem> arguments = new List<ArgumentsItem>();
        public int maxRetryCount = 1;
        public int priority = 2000;
        public int timeZone = 480;
        public string triggerType = "Once";
        public string videoRecordMode = VideoRecordMode.NeverRecord.ToString();
    }
    /// <summary>
    /// 获取任务执行结果
    /// </summary>
    public class runInstanceLogDetailsSimpleResult {
        public string jobId;
        public string runInstanceId;
        public string robotId;
        public string status;
        public int exitCode;
        public string createdAt;
        public string createdBy;
        public string id;
    }
    /// <summary>
    /// 获取任务 URI参数类
    /// </summary>
    public class JobQueryURIArgs
    {
        /// <summary>
        /// 页码，0开始
        /// </summary>
        public int PageIndex;
        /// <summary>
        /// 每页尺寸默认20
        /// </summary>
        public int PageSize;
        /// <summary>
        /// 流程部署或流程包名称
        /// </summary>
        public string Name;
        /// <summary>
        /// 流程包名称
        /// </summary>
        public string PackageName;
        /// <summary>
        /// 流程部署名称
        /// </summary>
        public string WorkflowName;
        /// <summary>
        /// 创建时间查询范围起点
        /// </summary>
        public DateTime StartCreateTime;
        /// <summary>
        /// 创建时间查询范围重点
        /// </summary>
        public DateTime EndCreateTime;
        /// <summary>
        /// 
        /// </summary>
        public DateTime StartRunTime;
        /// <summary>
        /// 
        /// </summary>
        public DateTime EndRunTime;
        /// <summary>
        /// 
        /// </summary>
        public DateTime StartFinishTime;
        /// <summary>
        /// 
        /// </summary>
        public DateTime EndFinishTime;
        /// <summary>
        /// 
        /// </summary>
        public EnumOrderBy Orderby;
        public bool IsDesc;
        public List<JobState> States;
        public EnumQueryTimeType QueryTimeType;
        public DateTime StartQueryTime;
        public DateTime EndQueryTime;
        public int timeZone;
        public string getURLArgs()
        {
            string valTemplete = $"http://rpaconsole.sinoreagent.com:8080/v2/jobs?queryTimeType=all";
            //Dictionary<string, string> args = new Dictionary<string, string>();
            if (!PageIndex.Equals(0))
            {
                valTemplete += $"&PageIndex={PageIndex}";
            }
            else {
                valTemplete += $"&PageIndex=0";
            }
            if (PageSize.Equals(0))
            {
                valTemplete += $"&PageSize=20";
            }
            else { 
                valTemplete += $"&PageSize={PageSize}";            
            }
            if (!string.IsNullOrEmpty(Name))
                valTemplete += $"&Name={Name}";
            if (!string.IsNullOrEmpty(PackageName))
                valTemplete += $"&PackageName={PackageName}";
            if (!string.IsNullOrEmpty(WorkflowName))
                valTemplete += $"&WorkflowName={WorkflowName}";
            if (!DateTime.MinValue.Equals(StartCreateTime))
                valTemplete += $"&StartCreateTime={StartCreateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";
            if (!DateTime.MinValue.Equals(EndCreateTime))
                valTemplete += $"&EndCreateTime={EndCreateTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";

            if (!DateTime.MinValue.Equals(StartRunTime))
                valTemplete += $"&StartRunTime={StartRunTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";
            if (!DateTime.MinValue.Equals(EndRunTime))
                valTemplete += $"&EndRunTime={EndRunTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";
            if (!DateTime.MinValue.Equals(StartFinishTime))
                valTemplete += $"&StartFinishTime={StartFinishTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";
            if (!DateTime.MinValue.Equals(EndFinishTime))
                valTemplete += $"&EndFinishTime={EndFinishTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";
            //if (Orderby != EnumOrderBy.StartedAt)
            //    valTemplete += $"&OrderBy={Orderby}";
            if (IsDesc)
                valTemplete += $"&IsDesc={IsDesc}";
            if (States != null)
            {
                try
                {
                    valTemplete += $"States={JsonConvert.SerializeObject(States)}";
                }
                catch (Exception)
                {
                    throw;
                }
            }

            if (!DateTime.MinValue.Equals(StartQueryTime))
                valTemplete += $"&StartQueryTime={StartQueryTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";
            if (!DateTime.MinValue.Equals(EndQueryTime))
                valTemplete += $"&EndQueryTime={EndQueryTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")}";
            if (timeZone.Equals(0))
                valTemplete += $"&timeZone=480";
            return valTemplete;
        }
    }
    /// <summary>
    /// 初次获取令牌
    /// </summary>
    [Serializable]
    public class AuthClass
    {
        public string access_token { get; set; }
        public int expires_in { get; set; }
        public string token_type { get; set; }
        public string refresh_token { get; set; }
        public string scope { get; set; }
    }
    /// <summary>
    /// 刷新过期令牌
    /// </summary>
    [Serializable]
    public class refreshAuthClass : AuthClass
    {
        public string id_token { get; set; }
    }
    /// <summary>
    /// 流程包信息
    /// </summary>
    [Serializable]
    public class packageInfo
    {
        public string id;
        public string name;
        public string description;
        public int totalDownloads;
        public string lastVersion;
        public string lastVersionID;
        public string eTag;
        public DateTime createdAt;
        public DateTime modifiedAt;
        public string createdBy;
        public string createdByName;
        public string modifiedBy;
        public string modifiedByName;
        public string[] tags;
        public string companyId;
        public string departmentID;
        public string resourceType;
        public string parentID;
        public string parentResourceType;
    }
    /// <summary>
    /// 获取所有流程包信息列表
    /// </summary>
    [Serializable]
    public class allPackageResponseInfo
    {
        public int count;
        public packageInfo[] list;
    }
    #region 获取日志 数据模型
    /// <summary>
    /// 流程部署运行封装类
    /// </summary>
    [Serializable]
    public class WorkflowDTO
    {
        public string id;
        public string departmentId;
        public string name;
        public List<ArgumentsItem> arguments;
        public string VideoRecordMode;
        public string PackageName;
        public string PackageId;
        public string PackageVersionId;
        public string PackageVersion;
        public string description;
        public string containingQueueId;
        public int priority;
        public int maxRetryCount;
        public string triggerPolicy;
        public string lastTriggeredAt;
        public int triggeredCount;
        public string createAt;
        public string createBy;
        public string createByName;
        public string modifiedAt;
        public string modifiedBy;
        public string modifiedByName;
        public string resourceType;
    }
    /// <summary>
    /// 流程运行参数类
    /// </summary>
    [Serializable]
    public class ArgumentsItem
    {
        /// <summary>
        /// 变量名称
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 变量类型
        /// </summary>
        public string type { get; set; }
        public bool allowEdit { get; set; }
        public string shortType { get; set; }
        /// <summary>
        /// 变量方向 In/Out
        /// </summary>
        public string direction { get; set; }
        /// <summary>
        /// 默认值
        /// </summary>
        public string defaultValue { get; set; }
    }
    public enum DataType { String, Bool }
    /// <summary>
    /// 执行记录  返回 数据模型
    /// </summary>
    [Serializable]
    public class JobDTOV2
    {
        /// <summary>
        /// 流程ID
        /// </summary>
        //public string workflowId { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 描述
        /// </summary>
        public string description { get; set; }
        /// <summary>
        ///　流程包ID
        /// </summary>
        public string packageId { get; set; }
        /// <summary>
        /// 流程包名称
        /// </summary>
        public string packageName { get; set; }
        /// <summary>
        /// 流程包版本
        /// </summary>
        public string packageVersion { get; set; }
        /// <summary>
        /// 流程包版本ID
        /// </summary>
        public string packageVersionId { get; set; }
        /// <summary>
        /// 流程包运行参数
        /// </summary>
        public List<ArgumentsItem> arguments { get; set; }
        /// <summary>
        /// 队列ID
        /// </summary>
        public string containingQueueId { get; set; }
        /// <summary>
        /// 优先级　默认２０００
        /// </summary>
        public int priority { get; set; }
        /// <summary>
        /// 状态
        /// </summary>
        public JobState state { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string videoRecordMode { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string message { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string deleted { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string lastRunInstaceId { get; set; }
        /// <summary>
        /// 天翼云-180
        /// </summary>
        public string lastRobotName { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string startedAt { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string finishedAt { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int retriedCount { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int maxRetryCount { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string departmentId { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string companyId { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string cronTriggerId { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string jobExecutionPolicy { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string createdAt { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string createdBy { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string createdByName { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string modifiedAt { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string id { get; set; }
    }
    [Serializable]
    public class JobWrapper
    {
        /// <summary>
        /// 数量
        /// </summary>
        public int count { get; set; }
        /// <summary>
        /// 返回数据集列表
        /// </summary>
        public List<JobDTOV2> list { get; set; }
    }
    [Serializable]
    public class RunInstanceLogEntryDTO
    {
        public string id;
        public string message;
        public List<string> robotId;
        public string logType;
        public string logLevel;
        public DateTime createdAt;
    }
    /// <summary>
    /// 翻页
    /// </summary>
    [Serializable]
    public class NextPageDTO
    {
        public string lastPartitionKey;
        public string lastRowKey;
    }
    /// <summary>
    /// 返回结果
    /// </summary>
    [Serializable]
    public class QueryLogResult
    {
        public NextPageDTO nextPage;
        public List<RunInstanceLogEntryDTO> data;
    }
    public class PackageVersionArgument { }
    public enum VideoRecordMode {NeverRecord=0, ReportOnlyWhenSucceeded=1, ReportOnlyWhenFailed =2, AlwaysRecord =3, AlwaysReport =4}
    public enum JobExecutionPolicy { }
    public enum JobState { Queued = 0, Allocated = 1, Running = 2, Failed = 3, Cancelled = 4, Succeeded = 5, Cancelling = 6 }
    #endregion 获取日志 数据模型

    #region 获取日志 信息获取

    /// <summary>
    /// 任务记录具体单条记录
    /// </summary>
    public class RunInstanceLogItem
    {
        /// <summary>
        /// 名称
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 超时时间单位
        /// </summary>
        public string executionTimeoutUnit { get; set; }
        /// <summary>
        /// 流程包ID
        /// </summary>
        public string packageId { get; set; }
        /// <summary>
        /// 流程包名
        /// </summary>
        public string packageName { get; set; }
        /// <summary>
        /// 流程包版本
        /// </summary>
        public string packageVersion { get; set; }
        /// <summary>
        /// 流程包版本ID
        /// </summary>
        public string packageVersionId { get; set; }
        /// <summary>
        /// 参数列表
        /// </summary>
        public List<ArgumentsItem> arguments { get; set; }
        /// <summary>
        /// 容器队列ID
        /// </summary>
        public string containingQueueId { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public bool isRobotDedicatedQueue { get; set; }
        /// <summary>
        /// 优先级
        /// </summary>
        public int priority { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int reversedPriority { get; set; }
        /// <summary>
        /// 状态
        /// </summary>
        public string state { get; set; }
        /// <summary>
        /// 上次状态
        /// </summary>
        public string lastState { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string source { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string videoRecordMode { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public bool deleted { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string lastRunInstaceId { get; set; }
        /// <summary>
        /// 国药试剂-财务(10.3.0.183)
        /// </summary>
        public string lastRobotName { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string lastRobotId { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string startedAt { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string finishedAt { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int retriedCount { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public int maxRetryCount { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string departmentId { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string companyId { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string jobStartupType { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string createdAt { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string createdBy { get; set; }
        /// <summary>
        /// 陈鸿杰
        /// </summary>
        public string createdByName { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string modifiedAt { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string modifiedBy { get; set; }
        /// <summary>
        /// 国药试剂-财务(10.3.0.183)
        /// </summary>
        public string modifiedByName { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string id { get; set; }
    }
    /// <summary>
    /// 任务记录容器（条目+单条记录列表）
    /// </summary>
    public class RunInstanceLogWrapper
    {
        /// <summary>
        /// 
        /// </summary>
        public int count { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public List<RunInstanceLogItem> list { get; set; }
    }

    #endregion 获取日志 信息获取

    #region 机器人管理
    #region 机器人管理 数据模型
    /// <summary>
    /// Robot信息基础单元
    /// </summary>
    [Serializable]
    [Obsolete]
    public class RobotDTO
    {
        public string id;
        public string departmentId;
        public string companyId;
        public List<string> tags;
        public string serverSku;
        public string serverSkuName;
        public string version;
        public RobotManagedStatus consoleManagement;
        public string clientSku;
        public string name;
        public string description;
        public string clientSecret;
        public RobotLIcenseStatus licenseStatus;
        public RobotStatus status;
        public DateTime createdAt;
        public string createdBy;
        public string createdByName;
        public DateTime modifiedAt;
        public string modifiedBy;
        public string updatedByName;
        public string connectionString;
    }
    /// <summary>
    /// Robot信息单元
    /// </summary>
    public class RobotDTOV2
    {
        /// <summary>
        /// ID
        /// </summary>
        public string id { get; set; }
        /// <summary>
        /// 部门ID
        /// </summary>
        public string departmentId { get; set; }
        /// <summary>
        /// 公司ID
        /// </summary>
        public string companyId { get; set; }
        /// <summary>
        /// Tags
        /// </summary>
        public List<string> tags { get; set; }
        /// <summary>
        /// 许可类型
        /// </summary>
        public string serverSku { get; set; }
        /// <summary>
        /// 许可类型名称
        /// </summary>
        public string serverSkuName { get; set; }
        /// <summary>
        /// 是否接收控制台调度
        /// </summary>
        public RobotManagedStatus consoleManagement { get; set; }
        /// <summary>
        /// 机器人名称
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// 客户端密钥
        /// </summary>
        public string clientSecret { get; set; }
        /// <summary>
        /// 许可状态
        /// </summary>
        public RobotLIcenseStatus licenseStatus { get; set; }
        /// <summary>
        /// 运行状态
        /// </summary>
        public string status { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        public string createdAt { get; set; }
        /// <summary>
        /// 创建人ID
        /// </summary>
        public string createdBy { get; set; }
        /// <summary>
        /// 创建人名称
        /// </summary>
        public string createdByName { get; set; }
        /// <summary>
        /// 上次修改时间
        /// </summary>
        public string modifiedAt { get; set; }
        /// <summary>
        /// 上次修改ID
        /// </summary>
        public string modifiedBy { get; set; }
        /// <summary>
        /// 上次修改人名称
        /// </summary>
        public string modifiedByName { get; set; }
        /// <summary>
        /// 连接字符串
        /// </summary>
        public string connectionString { get; set; }
        /// <summary>
        /// 上次心跳时间
        /// </summary>
        public DateTime lastHeartbeatTime { get; set; }
        /// <summary>
        /// 上级ID
        /// </summary>
        public string parentId { get; set; }
        /// <summary>
        /// 上级资源类型
        /// </summary>
        public string parentResourceType { get; set; }
        /// <summary>
        /// 资源类型
        /// </summary>
        public string resourceType { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public string thresholdSettings { get; set; }
        /// <summary>
        /// 监听状态
        /// </summary>
        public List<string> listeningStatus { get; set; }
        /// <summary>
        /// 管理状态
        /// </summary>
        public string maintainStatus { get; set; }
    }
    /// <summary>
    /// Robot信息单元容器
    /// </summary>
    public class RobotListWrapper
    {
        /// <summary>
        /// 数量
        /// </summary>
        public int count { get; set; }
        /// <summary>
        /// 所有机器人项列表
        /// </summary>
        public List<RobotDTOV2> list { get; set; }
    }
    public enum RobotManagedStatus { Managed = 0, Unmanaged = 1 }
    public enum RobotLIcenseStatus { Unlicensed = 0, ClientLicensed = 1, ServerLicensed = 2, DuplicateLicensed = 3, PoolLicensed = 4 }
    public enum RobotStatus { Disconnected = 0, Ready = 1, Busy = 2 }
    public enum RpaLicenseSku { Community = 1, Enterprise2 = 2, Enterprise3 = 3, Enterprise4 = 4, Enterprise_Floating = 8 }
    public enum EnumOrderBy { CreatedAt = 0, StartedAt = 1, FinishedAt = 2, Priority = 3 }
    public enum EnumQueryTimeType { CreatedAt = 0, StartedAt = 1, FinishedAt = 2, All = 3 }

    #endregion 机器人管理 数据模型


    #endregion 机器人管理

    #region 公司信息
    #region 公司列表

    /// <summary>
    /// 公司详细信息
    /// </summary>
    [Serializable]
    public class CompaniesItem
    {
        /// <summary>
        /// 公司用户的ID
        /// </summary>
        public string companyUserId { get; set; }
        /// <summary>
        /// 公司用户的名称
        /// </summary>
        public string companyUserName { get; set; }
        /// <summary>
        /// 数据ID
        /// </summary>
        public string id { get; set; }
        /// <summary>
        /// 资源类型（固定Company）
        /// </summary>
        public string resourceType { get; set; }
        /// <summary>
        /// 父资源类型（固定Company）
        /// </summary>
        public string parentResourceType { get; set; }
        /// <summary>
        /// 父级公司ID
        /// </summary>
        public string parentId { get; set; }
        /// <summary>
        /// 公司名称
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Tags
        /// </summary>
        public List<string> tags { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public Properties ?properties { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        public string createdAt { get; set; }
        /// <summary>
        /// 公司ID
        /// </summary>
        public string companyId { get; set; }
        /// <summary>
        /// 版本
        /// </summary>
        public string edition { get; set; }
        /// <summary>
        /// 相关域名
        /// </summary>
        public List<string> domains { get; set; }
    }
    /// <summary>
    /// 租户详细信息
    /// </summary>
    [Serializable]
    public class TenantsItem
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        public string userId { get; set; }
        /// <summary>
        /// 租户ID
        /// </summary>
        public string tenantId { get; set; }
        /// <summary>
        /// 是否为租户拥有者
        /// </summary>
        public string isOwner { get; set; }
        /// <summary>
        /// 用户状态（Active Inactive Banned）
        /// </summary>
        public string status { get; set; }
        /// <summary>
        /// 租户名称
        /// </summary>
        public string tenantName { get; set; }
        /// <summary>
        /// 用户显示姓名
        /// </summary>
        public string displayName { get; set; }
        /// <summary>
        /// 租户类型（Enterprise Community）
        /// </summary>
        public string edition { get; set; }
        /// <summary>
        /// 租户域名信息
        /// </summary>
        public List<string> domains { get; set; }
    }
    /// <summary>
    /// 公司列表请求结果容器
    /// </summary>
    [Serializable]
    public class CompanyListWrapper
    {
        /// <summary>
        /// 公司信息列表
        /// </summary>
        public List<CompaniesItem> companies { get; set; }
        /// <summary>
        /// 租户信息列表
        /// </summary>
        public List<TenantsItem> tenants { get; set; }
    }
    #endregion 公司列表
    #region 部门树
    public class Properties
    {
       
    }
    public enum ParamsDirection { In, Out }
    /// <summary>
    /// 子部门
    /// </summary>
    [Serializable]
    public class ChildrenDepartmentItem
    {
        /// <summary>
        /// 子部门
        /// </summary>
        public List<ChildrenDepartmentItem> children { get; set; }
        /// <summary>
        /// 部门全路径   斜杠/分割
        /// </summary>
        public string departmentPath { get; set; }
        /// <summary>
        /// 用户数量
        /// </summary>
        public int userCount { get; set; }
        /// <summary>
        /// 部门ID
        /// </summary>
        public string id { get; set; }
        /// <summary>
        /// 资源类型 默认Department
        /// </summary>
        public string resourceType { get; set; }
        /// <summary>
        /// 部门编号，与ID一致
        /// </summary>
        public string departmentId { get; set; }
        /// <summary>
        /// 父级资源类型，默认Department
        /// </summary>
        public string parentResourceType { get; set; }
        /// <summary>
        /// 父级ID
        /// </summary>
        public string parentId { get; set; }
        /// <summary>
        /// 部门名称
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Tags 可为空 
        /// </summary>
        public List<string> tags { get; set; }
        /// <summary>
        /// 属性 可为空
        /// </summary>
        public Properties ?properties { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        public string createdAt { get; set; }
        /// <summary>
        /// 公司ID
        /// </summary>
        public string companyId { get; set; }
    }
    /// <summary>
    /// 根部门
    /// </summary>
    [Serializable]
    public class RootDepartmentItem
    {
        /// <summary>
        /// 部门容器
        /// </summary>
        public List<ChildrenDepartmentItem> children { get; set; }
        /// <summary>
        /// 部门路径 一级部门名称
        /// </summary>
        public string departmentPath { get; set; }
        /// <summary>
        /// 用户数量
        /// </summary>
        public int userCount { get; set; }
        /// <summary>
        /// 部门ID
        /// </summary>
        public string id { get; set; }
        /// <summary>
        /// 部门资源类型 Department
        /// </summary>
        public string resourceType { get; set; }
        /// <summary>
        /// 部门ID
        /// </summary>
        public string departmentId { get; set; }
        /// <summary>
        /// 父级资源类型，顶级部门的为Company,二级以下为Department
        /// </summary>
        public string parentResourceType { get; set; }
        /// <summary>
        /// 父级ID    顶级部门的为公司CompanyID,二级以下为DepartmentID 
        /// </summary>
        public string parentId { get; set; }
        /// <summary>
        /// 名称
        /// </summary>
        public string name { get; set; }
        /// <summary>
        /// Tags
        /// </summary>
        public List<string> tags { get; set; }
        /// <summary>
        /// 属性
        /// </summary>
        public Properties properties { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        public string createdAt { get; set; }
        /// <summary>
        /// 公司ID
        /// </summary>
        public string companyId { get; set; }
    }
    /// <summary>
    /// 部门树容器
    /// </summary>
    [Serializable]
    public class DepartmentTreeWrapper
    {
        /// <summary>
        /// 
        /// </summary>
        public int count { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public RootDepartmentItem rootDepartment { get; set; }
    }
    #endregion 部门树
    #endregion 公司信息
    #endregion 容器类
}
