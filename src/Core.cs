using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
using Microsoft.Win32;

namespace TimezoneSwitcher {
 public static class Data {
  public static Dictionary<string,object> Parse(string s) { return new JavaScriptSerializer { MaxJsonLength=262144 }.Deserialize<Dictionary<string,object>>(s); }
  public static string S(Dictionary<string,object> d,string k) { object v; return d!=null && d.TryGetValue(k,out v) && v is string ? (string)v : ""; }
  public static bool? B(Dictionary<string,object> d,string k) { object v; return d!=null && d.TryGetValue(k,out v) && v is bool ? (bool?)v : null; }
  public static Dictionary<string,object> O(Dictionary<string,object> d,string k) { object v; return d!=null && d.TryGetValue(k,out v) ? v as Dictionary<string,object> : null; }
  public static bool IP(string value) { IPAddress ip; return IPAddress.TryParse(value,out ip) && !IPAddress.IsLoopback(ip) && !String.IsNullOrWhiteSpace(value); }
 }
 public class Reply { public string Body=""; public bool Responded,Unreachable; public string Error=""; }
 public static class Http {
  public static async Task<Reply> Get(string url,int timeout) {
   var r=new Reply();
   try {
    var q=(HttpWebRequest)WebRequest.Create(url); q.Proxy=null; q.AllowAutoRedirect=false; q.Timeout=timeout; q.ReadWriteTimeout=timeout; q.UserAgent="TimezoneSwitcher/1.0";
    // GetResponseAsync does not honor Timeout on .NET Framework. Abort explicitly.
    using(var timer=new System.Threading.Timer(delegate {q.Abort();},null,timeout,System.Threading.Timeout.Infinite)) {
     using(var response=(HttpWebResponse)await q.GetResponseAsync()) {
      r.Responded=true;
      using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)) {
       char[] buf=new char[4096]; var text=new StringBuilder(); int n;
       while((n=await reader.ReadAsync(buf,0,buf.Length))>0) {text.Append(buf,0,n); if(text.Length>262144) throw new IOException("Response too large");}
       r.Body=text.ToString();
      }
     }
    }
   } catch(WebException e) {
    r.Responded=e.Response!=null; if(e.Response!=null)e.Response.Close();
    r.Unreachable=!r.Responded && (e.Status==WebExceptionStatus.ConnectFailure || e.Status==WebExceptionStatus.NameResolutionFailure || e.Status==WebExceptionStatus.Timeout || e.Status==WebExceptionStatus.RequestCanceled);
    r.Error=e.Status.ToString();
   } catch(Exception e) {r.Error=e.GetType().Name;}
   return r;
  }
 }
 public class KordonState {
  public bool Available,Safe,ObservationValid,Ambiguous; public string IP="",IP6="",Country="",Version="",Reason="",Status="";
  public string Key {get{return Version+"|"+IP+"|"+IP6+"|"+Country+"|"+ObservationValid+"|"+Ambiguous;}}
  public static KordonState FromJson(string text) {
   var k=new KordonState(); var d=Data.Parse(text); k.Version=Data.S(d,"kordon"); k.IP=Data.S(d,"ip"); k.IP6=Data.S(d,"ip6"); k.Country=Data.S(Data.O(d,"country"),"code");
   k.Available=k.Version.StartsWith("1.4.",StringComparison.Ordinal);
   var reason=Data.S(d,"reason"); var status=Data.S(d,"status");
   bool split=Data.B(d,"split")==true || Data.B(d,"split_routing")==true || Data.B(d,"split_tunnel")==true || Data.B(d,"proxy")==true;
   k.Status=status;
   k.Ambiguous=split || Regex.IsMatch(status+" "+reason,@"split|proxy|pac\b|leak|ambig|disagree|раздельн|прокси|утеч|неоднознач|расхожд",RegexOptions.IgnoreCase);
   // App protection permission is not a verdict on the observed IP's timezone.
   // A paused/denied home IP remains usable only after independent geo + route validation.
   k.ObservationValid=k.Available && Data.B(d,"pause").HasValue && status!="" && Data.IP(k.IP) && Regex.IsMatch(k.Country,"^[A-Z]{2}$");
   k.Safe=k.Available && Data.B(d,"pause")==false && status=="ok" && reason=="" && !split && Data.IP(k.IP) && Regex.IsMatch(k.Country,"^[A-Z]{2}$");
   k.Reason=!k.Available?"Версия API Kordon не проверена":(!k.ObservationValid?"Kordon ещё не сообщает проверяемый IP и страну":(k.Ambiguous?"Kordon сообщает неоднозначный маршрут или утечку":""));
   return k;
  }
  public static async Task<KordonState> Read() {
   foreach(var port in new[]{47833,41833,35833,29833}) {
    var r=await Http.Get("http://127.0.0.1:"+port+"/ext/v1/state",1300);
    if(r.Body!="") try {var k=FromJson(r.Body); if(k.Version!="")return k;} catch {}
   }
   return new KordonState {Reason="Kordon недоступен. Запустите Kordon 1.4.x."};
  }
 }
 public class Geo {
  public string IP="",Zone="",Country=""; public bool Valid;
  public static Geo Parse(string body,bool who) {
   var g=new Geo(); try {var d=Data.Parse(body); g.IP=Data.S(d,"ip"); g.Country=Data.S(d,"country_code"); g.Zone=who?Data.S(Data.O(d,"timezone"),"id"):Data.S(d,"timezone");
    g.Valid=Data.IP(g.IP) && g.Zone.Contains("/") && Regex.IsMatch(g.Country,"^[A-Z]{2}$") && (who?Data.B(d,"success")==true:Data.B(d,"error")!=true);
   }catch{} return g;
  }
 }
 public static class Zones {
  public const string Moscow="Russian Standard Time", Berlin="W. Europe Standard Time";
  static Dictionary<string,string> map;
  public static string Windows(string iana) {
   if(map==null) {
    var m=new Dictionary<string,string>(StringComparer.Ordinal); var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Ignore, XmlResolver=null};
    using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("windowsZones.xml")) using(var reader=XmlReader.Create(s,settings)) {
     while(reader.Read()) if(reader.NodeType==XmlNodeType.Element && reader.Name=="mapZone") {var win=reader.GetAttribute("other"); foreach(var id in reader.GetAttribute("type").Split(' '))m[id]=win;}
    } map=m;
   }
   string value; if(!map.TryGetValue(iana,out value))return null;
   try {TimeZoneInfo.FindSystemTimeZoneById(value);return value;}catch{return null;}
  }
 }
 public class Route4 {public uint Dest,Mask;public string Address;public int Metric;}
 public class NetworkState {
  public string Key="",Problem=""; public bool OfflineLinks;
  public static bool Private(uint n) {return (n>>24)==10 || (n>>24)==127 || (n>>24)==0 || (n>>28)>=14 || (n>>16)==0xC0A8 || (n>>20)==0xAC1 || (n>>16)==0xA9FE;}
  public static uint Number(string ip) {var b=IPAddress.Parse(ip).GetAddressBytes();return ((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3];}
  public static List<Route4> Parse4(string text) {
   var list=new List<Route4>();
   foreach(Match m in Regex.Matches(text,@"(?m)^\s*(\d+\.\d+\.\d+\.\d+)\s+(\d+\.\d+\.\d+\.\d+)\s+\S+\s+(\d+\.\d+\.\d+\.\d+)\s+(\d+)\s*$")) {
    list.Add(new Route4 {Dest=Number(m.Groups[1].Value),Mask=Number(m.Groups[2].Value),Address=m.Groups[3].Value,Metric=Int32.Parse(m.Groups[4].Value)});
   }return list;
  }
  public static string RouteProblem(List<Route4> routes,string exit,out string selected) {
   selected=""; if(routes.Count==0)return "Не удалось прочитать маршруты Windows";
   // Evaluate both halves: supports the common pair of /1 VPN routes.
   foreach(var n in new[]{Number("1.1.1.1"),Number("129.1.1.1")}) {
    var candidates=routes.Where(x=>(n&x.Mask)==x.Dest).OrderByDescending(x=>x.Mask).ThenBy(x=>x.Metric).ToArray();
    if(candidates.Length==0)return "Нет полного маршрута в интернет";
    var best=candidates[0]; if(candidates.Any(x=>x.Mask==best.Mask && x.Metric==best.Metric && x.Address!=best.Address))return "Равнозначные маршруты через разные интерфейсы";
    if(selected!=""&&selected!=best.Address)return "Раздельная маршрутизация: разные пути IPv4"; selected=best.Address;
   }
   uint endpoint=Data.IP(exit)&&IPAddress.Parse(exit).AddressFamily==AddressFamily.InterNetwork?Number(exit):0;
   foreach(var r in routes) {
    if(r.Mask==0 || r.Address==selected || Private(r.Dest))continue;
    // A single host route to the very same exit/server is needed by many tunnels.
    if(r.Mask==UInt32.MaxValue && r.Dest==endpoint)continue;
    return "Раздельная маршрутизация: публичные маршруты вне основного интерфейса";
   }return "";
  }
  public static string Run(string name,string args) {
   var start=new ProcessStartInfo(name,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   using(var p=Process.Start(start)) {var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(6000)){p.Kill();throw new IOException("Command timeout");}Task.WaitAll(output,error);if(p.ExitCode!=0)throw new IOException("Command failed");return output.Result;}
  }
  public static NetworkState Read(string exit) {
   var s=new NetworkState(); try {
    var adapters=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up && x.NetworkInterfaceType!=NetworkInterfaceType.Loopback).ToArray();
    s.OfflineLinks=!adapters.Any(x=>x.GetIPProperties().UnicastAddresses.Any(a=>a.Address.AddressFamily==AddressFamily.InterNetwork || (a.Address.AddressFamily==AddressFamily.InterNetworkV6&&!a.Address.IsIPv6LinkLocal)));
    bool proxy=false;
    using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings")) if(k!=null)proxy=Convert.ToInt32(k.GetValue("ProxyEnable",0))!=0 || !String.IsNullOrEmpty(k.GetValue("AutoConfigURL","").ToString());
    foreach(var key in new[]{"HTTP_PROXY","HTTPS_PROXY","ALL_PROXY"})if(!String.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))proxy=true;
    // Proxy/PAC paths cannot be equated with machine-wide routes. Never probe around them.
    if(proxy) {s.Problem="Прокси или PAC: общий часовой пояс неоднозначен";s.Key="proxy";return s;}
    var r4=Run(Path.Combine(Environment.SystemDirectory,"route.exe"),"print -4");
    var r6=Run(Path.Combine(Environment.SystemDirectory,"route.exe"),"print -6");
    var list=Parse4(r4);string selected;s.Problem=RouteProblem(list,exit,out selected);
    int index=-1;
    foreach(var a in adapters) if(a.GetIPProperties().UnicastAddresses.Any(u=>u.Address.ToString()==selected))index=a.GetIPProperties().GetIPv4Properties().Index;
    foreach(Match m in Regex.Matches(r6,@"(?m)^\s*(\d+)\s+\d+\s+([0-9a-fA-F:]+)/(\d+)\s+")) {
     IPAddress ip; if(!IPAddress.TryParse(m.Groups[2].Value,out ip))continue; int prefix=Int32.Parse(m.Groups[3].Value); var b=ip.GetAddressBytes();
     bool global=prefix==0 || (b[0]&0xE0)==0x20;
     if(global && Int32.Parse(m.Groups[1].Value)!=index)s.Problem="IPv4 и IPv6 используют разные интерфейсы";
    }
    var lines=list.Select(r=>r.Dest+"/"+r.Mask+"/"+r.Address+"/"+r.Metric).OrderBy(x=>x);
    s.Key=String.Join(";",lines)+"|"+Regex.Replace(r6,@"\s+"," ")+"|"+String.Join(";",adapters.Select(x=>x.Id).OrderBy(x=>x));
   }catch(Exception e){s.Problem="Маршруты недоступны: "+e.GetType().Name;s.Key="error";}
   return s;
  }
 }
 public class Observation {
  public bool Offline,Changed;public KordonState K=new KordonState();public Geo A=new Geo(),B=new Geo(),V6A=new Geo(),V6B=new Geo();public NetworkState Net=new NetworkState();public string Error="";
  public string Key {get{return Offline?"offline":K.Key+"|"+Net.Key+"|"+A.Zone+"|"+A.IP;}}
 }
 public class Decision {public string Zone,Reason;public string Key;public bool Ready;}
 public static class Policy {
  public static Decision Evaluate(Observation o,bool auto) {
   var d=new Decision {Key=o.Key};
   if(!auto){d.Reason="Ручной режим имеет приоритет";return d;}
   if(o.Changed){d.Reason="Сеть изменилась во время проверки";return d;}
   if(o.Offline){d.Zone=Zones.Moscow;d.Reason="Нет интернета: Москва";return d;}
   if(o.Error!=""){d.Reason=o.Error;return d;}
   if(o.Net.Problem!=""){d.Reason=o.Net.Problem;return d;}
   if(!o.K.Available||!o.K.ObservationValid||o.K.Ambiguous){d.Reason=o.K.Reason;return d;}
   if(!o.A.Valid||!o.B.Valid){d.Reason="Геолокация недоступна. Текущий пояс сохранён.";return d;}
   if(o.A.IP!=o.K.IP || o.B.IP!=o.K.IP || o.A.Zone!=o.B.Zone || o.A.Country!=o.B.Country || o.A.Country!=o.K.Country){d.Reason="Kordon и сервисы расходятся: возможна раздельная маршрутизация";return d;}
   if(o.K.IP6!="" && (!o.V6A.Valid || !o.V6B.Valid || o.V6A.IP!=o.K.IP6 || o.V6B.IP!=o.K.IP6 || o.V6A.Zone!=o.A.Zone || o.V6B.Zone!=o.A.Zone || o.V6A.Country!=o.A.Country || o.V6B.Country!=o.A.Country)){d.Reason="Регион внешнего IPv6 не подтверждён или расходится с IPv4";return d;}
   d.Zone=Zones.Windows(o.A.Zone);d.Reason=d.Zone==null?"Регион IANA не поддерживается этой Windows":o.A.Zone+" · подтверждено двумя сервисами";return d;
  }
 }
 public class Stability {
  string key="";DateTime since; public void Reset(){key="";}
  public Decision Accept(Decision d,DateTime now) {
   if(d.Zone==null){Reset();return d;}
   string candidate=d.Zone+"|"+d.Key;
   if(candidate!=key){key=candidate;since=now;return d;}
   d.Ready=(now-since).TotalSeconds>=3;return d;
  }
 }
 public static class Probe {
  public static async Task<Observation> Read() {
   var o=new Observation();o.K=await KordonState.Read();o.Net=await Task.Run(()=>NetworkState.Read(o.K.IP));
   if(o.Net.Problem.StartsWith("Прокси"))return o;
   var a=Http.Get("https://ipwho.is/",7000);var b=Http.Get("https://ipapi.co/json/",7000);await Task.WhenAll(a,b);
   o.A=Geo.Parse(a.Result.Body,true);o.B=Geo.Parse(b.Result.Body,false);
   if(Data.IP(o.K.IP6) && o.A.Valid && o.B.Valid) {
    var v6a=Http.Get("https://ipwho.is/"+Uri.EscapeDataString(o.K.IP6),7000);var v6b=Http.Get("https://ipapi.co/"+Uri.EscapeDataString(o.K.IP6)+"/json/",7000);await Task.WhenAll(v6a,v6b);
    o.V6A=Geo.Parse(v6a.Result.Body,true);o.V6B=Geo.Parse(v6b.Result.Body,false);
   }
   if(!a.Result.Responded&&!b.Result.Responded) {
    var c=Http.Get("https://www.microsoft.com/",6000);var d=Http.Get("https://www.cloudflare.com/cdn-cgi/trace",6000);await Task.WhenAll(c,d);
    o.Offline=(o.Net.OfflineLinks || (a.Result.Unreachable&&b.Result.Unreachable&&c.Result.Unreachable&&d.Result.Unreachable));
    if(!o.Offline && !c.Result.Responded && !d.Result.Responded)o.Error="Доступ к сети не подтверждён (возможна ошибка TLS). Пояс сохранён.";
   }
   var after=await KordonState.Read();var netAfter=await Task.Run(()=>NetworkState.Read(after.IP));
   o.Changed=o.K.Key!=after.Key||o.Net.Key!=netAfter.Key;
   return o;
  }
 }
}
namespace TimezoneSwitcher {
 public class ZoneOption {
  public string Id {get;private set;} public string Label {get;private set;} public string Search {get;private set;}
  public ZoneOption(string id){Id=id;var z=System.TimeZoneInfo.FindSystemTimeZoneById(id);Label=ZoneCatalog.Name(id)+" · "+z.DisplayName;Search=Label+" "+id;}
 }
 public static class ZoneCatalog {
  static readonly System.Collections.Generic.Dictionary<string,string> names=new System.Collections.Generic.Dictionary<string,string>{
   {"Russian Standard Time","Россия · Москва"},{"W. Europe Standard Time","Германия · Берлин / Италия · Рим"},
   {"GMT Standard Time","Великобритания · Лондон"},{"Romance Standard Time","Франция · Париж / Испания · Мадрид"},
   {"Turkey Standard Time","Турция · Стамбул"},{"Arabian Standard Time","ОАЭ · Дубай"},
   {"Georgian Standard Time","Грузия · Тбилиси"},{"Armenian Standard Time","Армения · Ереван"},
   {"Central Asia Standard Time","Казахстан · Астана"},{"Israel Standard Time","Израиль · Иерусалим"},
   {"Eastern Standard Time","США · Нью-Йорк"},{"Pacific Standard Time","США · Лос-Анджелес"},
   {"Central Standard Time","США · Чикаго"},{"Mountain Standard Time","США · Денвер"},
   {"Tokyo Standard Time","Япония · Токио"},{"China Standard Time","Китай · Пекин"},
   {"India Standard Time","Индия · Нью-Дели"},{"SE Asia Standard Time","Таиланд · Бангкок / Вьетнам · Ханой"},
   {"Singapore Standard Time","Сингапур / Малайзия · Куала-Лумпур"},{"Korea Standard Time","Южная Корея · Сеул"},
   {"AUS Eastern Standard Time","Австралия · Сидней"},{"New Zealand Standard Time","Новая Зеландия · Окленд"},
   {"E. South America Standard Time","Бразилия · Сан-Паулу"},{"South Africa Standard Time","ЮАР · Йоханнесбург"},
   {"Egypt Standard Time","Египет · Каир"},{"UTC","UTC"}
  };
  public static bool Valid(string id){try{if(string.IsNullOrEmpty(id))return false;System.TimeZoneInfo.FindSystemTimeZoneById(id);return true;}catch{return false;}}
  public static string Name(string id){string name;if(names.TryGetValue(id,out name))return name;try{return System.TimeZoneInfo.FindSystemTimeZoneById(id).DisplayName;}catch{return id;}}
  public static string[] Normalize(string[] ids){return (ids??new[]{Zones.Moscow,Zones.Berlin}).Where(Valid).Distinct().ToArray();}
 }
}
