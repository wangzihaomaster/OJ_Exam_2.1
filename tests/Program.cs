using ProctorCommon;

int pass = 0, fail = 0;
void Check(string name, bool ok)
{
    Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name);
    if (ok) pass++; else fail++;
}

// ================= A. 加解密往返（学生端导出 -> 教师端解密） =================
var info = new DisciplineInfo
{
    StudentName = "张三", School = "某某中学", IdNumber = "110101200801011234",
    SeatNumber = "A-12", ContestName = "NOI 2026 初赛",
    RecordStart = new DateTime(2026, 11, 1, 9, 0, 0),
    RecordEnd   = new DateTime(2026, 11, 1, 11, 30, 0),
    VideoPath = @"D:\Proctor\record_20261101", VideoFileSha256 = "abc123",
};
info.Violations.Add(new ViolationRecord { Time = new DateTime(2026,11,1,10,5,0), Pid = 4321, ProcessName = "WeChat.exe", WindowTitle = "微信" });
info.Violations.Add(new ViolationRecord { Time = new DateTime(2026,11,1,10,40,0), Pid = 777, ProcessName = "QQ.exe", WindowTitle = "QQ 聊天" });
info.Submissions.Add(new ProblemInfo { Contest="NOI2026", ProblemId="P1001", ProblemName="A+B", Language="C++14", Code="#include<bits/stdc++.h>\nint main(){}", SubmitTime=new DateTime(2026,11,1,9,30,0) });

string json = info.ToJson();
Check("JSON 序列化包含中文姓名", json.Contains("张三"));
Check("JSON 可反序列化", DisciplineInfo.FromJson(json).Violations.Count == 2);

string password = "考场口令-2026";
byte[] packet = DisciplineCrypto.Encrypt(json, password);
string decrypted = DisciplineCrypto.Decrypt(packet, password);
Check("解密后与原文一致", decrypted == json);
Check("解密对象字段一致", DisciplineInfo.FromJson(decrypted).StudentName == "张三");

bool wrongPwdRejected=false; try { DisciplineCrypto.Decrypt(packet, "错误口令"); } catch (DisciplineDecryptException) { wrongPwdRejected=true; }
Check("错误口令被拒绝", wrongPwdRejected);

var tampered = (byte[])packet.Clone(); tampered[tampered.Length/2] ^= 0xFF;
bool tamperDetected=false; try { DisciplineCrypto.Decrypt(tampered, password); } catch (DisciplineDecryptException) { tamperDetected=true; }
Check("篡改文件被 HMAC 检出", tamperDetected);

bool badMagicRejected=false; try { DisciplineCrypto.Decrypt(System.Text.Encoding.UTF8.GetBytes("not a disc file"), password); } catch (DisciplineDecryptException) { badMagicRejected=true; }
Check("非法魔数被拒绝", badMagicRejected);

// 模拟落盘再读取（教师端 LoadPacket 路径）
var tmp = Path.Combine(Path.GetTempPath(), "sample.disc");
DisciplineCrypto.SavePacket(tmp, packet);
Check("磁盘往返一致", DisciplineCrypto.Decrypt(DisciplineCrypto.LoadPacket(tmp), password) == json);
File.Delete(tmp);

Console.WriteLine($"\n[核心库] {pass} 通过, {fail} 失败");

// ================= B. 白名单规则（对应需求 2 的例外清单） =================
// 注意：这里复刻学生端 AppWhitelist 的规则做离线验证（逻辑必须与其保持一致）。
static bool IsAllowed(string processName, string windowTitle)
{
    string[] procKw = {
        "idea","clion","pycharm","webstorm","goland","rider","datagrip",
        "devenv","vcexpress","vswdexpress","devcpp","smallcpp",
        "codeblocks","qtcreator","eclipse","netbeans","studio64","vscode","codium","thonny","spyder","jetbrains",
        "mspaint","paint","paint3d","photoshop",
        "chrome","firefox","msedge","edge","iexplore","opera","brave","vivaldi","safari",
        "explorer","winlogon","csrss","svchost","services","lsass","smss",
        "dwm","taskhostw","runtimebroker","searchhost","ctfmon","dllhost",
        "conhost","audiodg","spoolsv","sihost","startmenuexperiencehost",
        "textinputhost","applicationframehost","shellexperiencehost","everything",
    };
    string[] titleKw = {
        "intellij idea","visual studio","dev-c++","devc++","code::blocks",
        "visual c++","vc++","paint","画图","google chrome","mozilla firefox",
        "microsoft edge","safari","opera","visual studio code","vs code",
    };
    string[] self = { "监考学生端","studentclient","proctorclient","proctor" };
    var p = (processName??"").ToLowerInvariant(); var t = (windowTitle??"").ToLowerInvariant();
    return self.Any(s=>p.Contains(s)) || procKw.Any(k=>p.Contains(k)) || titleKw.Any(k=>t.Contains(k));
}

// 应放行（题目要求的例外：IDEA、Dev-C++、Visual C++、画图、浏览器）
foreach (var (proc,title) in new[]{
    ("idea64","IntelliJ IDEA"), ("devcpp","Dev-C++ 5.11"), ("claw1","Dev-C++"),
    ("devenv","Microsoft Visual Studio 2022"), ("VC_Win32_Exe","Visual C++"),
    ("mspaint","无标题 - 画图"), ("chrome","Google Chrome"), ("firefox","Mozilla Firefox"),
    ("msedge","Microsoft Edge"), ("Code","Visual Studio Code")})
    Check($"放行: {proc} ({title})", IsAllowed(proc,title));

// 应拦截（其它一切软件）
foreach (var (proc,title) in new[]{
    ("WeChat","微信"), ("QQ","QQ 聊天"), ("Telegram","Telegram"), ("Game","英雄联盟"),
    ("steam","Steam"), ("dd","钉钉"), ("feishu","飞书"), ("potplayer","PotPlayer")})
    Check($"拦截: {proc} ({title})", !IsAllowed(proc,title));

Console.WriteLine($"\n总计: {pass} 通过, {fail} 失败");
return fail == 0 ? 0 : 1;
