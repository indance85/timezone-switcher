using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace TimezoneSwitcher {
 public static class Tests {
  static List<string> lines=new List<string>();static int failed;
  static void Test(string name,Action body){try{body();lines.Add("PASS "+name);}catch(Exception e){failed++;lines.Add("FAIL "+name+": "+e.Message);}}
  static void Assert(bool value){if(!value)throw new Exception("Assertion failed");}
  static Observation Good(){return new Observation {K=new KordonState {Available=true,Safe=true,ObservationValid=true,Version="1.4.2",IP="203.0.113.10",Country="DE"},A=new Geo {Valid=true,IP="203.0.113.10",Country="DE",Zone="Europe/Berlin"},B=new Geo {Valid=true,IP="203.0.113.10",Country="DE",Zone="Europe/Berlin"}};}
  static Observation Home(){var o=Good();o.K=KordonState.FromJson("{\"kordon\":\"1.4.2\",\"pause\":true,\"status\":\"blocked\",\"reason\":\"IP is not allowed\",\"ip\":\"198.51.100.10\",\"country\":{\"code\":\"RU\"}}");o.A=new Geo{Valid=true,IP=o.K.IP,Country="RU",Zone="Europe/Moscow"};o.B=new Geo{Valid=true,IP=o.K.IP,Country="RU",Zone="Europe/Moscow"};return o;}
  public static int Run(string path) {
   Test("Favorite migration defaults",()=>Assert(ZoneCatalog.Normalize(null).Length==2));
   Test("Favorites reject unknown and duplicates",()=>Assert(ZoneCatalog.Normalize(new[]{"Tokyo Standard Time","invalid",null,"Tokyo Standard Time"}).Length==1));
   Test("Empty favorites stay empty",()=>Assert(ZoneCatalog.Normalize(new string[0]).Length==0));
   Test("Country search aliases",()=>Assert(new ZoneOption("India Standard Time").Search.Contains("Индия")));
   Test("Fractional offset retained",()=>Assert(TimeZoneInfo.FindSystemTimeZoneById("India Standard Time").BaseUtcOffset.TotalMinutes==330));
   Test("CLDR Berlin",()=>Assert(Zones.Windows("Europe/Berlin")==Zones.Berlin));
   Test("CLDR Moscow",()=>Assert(Zones.Windows("Europe/Moscow")==Zones.Moscow));
   Test("Region within Russia",()=>Assert(Zones.Windows("Asia/Vladivostok")!="Russian Standard Time"));
   Test("Region within USA",()=>Assert(Zones.Windows("America/Los_Angeles")!=Zones.Windows("America/New_York")));
   Test("Unknown IANA holds",()=>Assert(Zones.Windows("MadeUp/Zone")==null));
   Test("Berlin DST winter/summer",()=>{var z=TimeZoneInfo.FindSystemTimeZoneById(Zones.Berlin);Assert(z.GetUtcOffset(new DateTime(2026,1,15,12,0,0,DateTimeKind.Utc)).TotalHours==1);Assert(z.GetUtcOffset(new DateTime(2026,7,15,12,0,0,DateTimeKind.Utc)).TotalHours==2);});
   Test("Agreement selects Berlin",()=>Assert(Policy.Evaluate(Good(),true).Zone==Zones.Berlin));
   Test("Manual dominates online",()=>Assert(Policy.Evaluate(Good(),false).Zone==null));
   Test("Manual dominates offline",()=>{var o=Good();o.Offline=true;Assert(Policy.Evaluate(o,false).Zone==null);});
   Test("Offline selects Moscow",()=>{var o=Good();o.Offline=true;Assert(Policy.Evaluate(o,true).Zone==Zones.Moscow);});
   Test("API outage is not offline",()=>{var o=Good();o.A.Valid=false;o.B.Valid=false;Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("One provider failure holds",()=>{var o=Good();o.B.Valid=false;Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("Different timezone holds",()=>{var o=Good();o.B.Zone="Europe/Moscow";Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("Different exit IP holds",()=>{var o=Good();o.B.IP="8.8.8.8";Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("Different country holds",()=>{var o=Good();o.K.Country="RU";Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("Kordon absent holds",()=>{var o=Good();o.K.Available=false;Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("Kordon protection pause does not veto confirmed region",()=>{var o=Good();o.K.Safe=false;Assert(Policy.Evaluate(o,true).Zone==Zones.Berlin);});
   Test("VPN off: denied home IP selects Moscow after confirmation",()=>{var o=Home();Assert(!o.K.Safe&&o.K.ObservationValid);string selected;o.Net.Problem=NetworkState.RouteProblem(NetworkState.Parse4("0.0.0.0 0.0.0.0 192.168.0.1 192.168.0.130 25\n"),o.K.IP,out selected);var s=new Stability();var now=DateTime.UtcNow;var a=s.Accept(Policy.Evaluate(o,true),now);Assert(a.Zone==Zones.Moscow&&!a.Ready);var b=s.Accept(Policy.Evaluate(o,true),now.AddSeconds(3));Assert(b.Zone==Zones.Moscow&&b.Ready);});
   Test("VPN off: manual still dominates",()=>Assert(Policy.Evaluate(Home(),false).Zone==null));
   Test("VPN off: stale Kordon IP still holds",()=>{var o=Home();o.K.IP="203.0.113.10";Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("VPN off: disagreeing providers still hold",()=>{var o=Home();o.B.Zone="Asia/Vladivostok";Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("VPN off: split routes still hold",()=>{var o=Home();o.Net.Problem="Split";Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("VPN off: missing geolocation still holds",()=>{var o=Home();o.A.Valid=false;Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("Kordon explicit ambiguity still holds",()=>{var o=Good();o.K=KordonState.FromJson("{\"kordon\":\"1.4.2\",\"pause\":true,\"status\":\"blocked\",\"reason\":\"split routing\",\"ip\":\"203.0.113.10\",\"country\":{\"code\":\"DE\"}}");Assert(o.K.Ambiguous&&Policy.Evaluate(o,true).Zone==null);});
   Test("Malformed Kordon observation still holds",()=>{var o=Home();o.K.ObservationValid=false;Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("Split tunnel holds",()=>{var o=Good();o.Net.Problem="Split";Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("TLS uncertainty holds",()=>{var o=Good();o.Error="TLS";Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("Network change invalidates online",()=>{var o=Good();o.Changed=true;Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("Network change invalidates offline",()=>{var o=Good();o.Changed=true;o.Offline=true;Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("IPv6 unconfirmed holds",()=>{var o=Good();o.K.IP6="2001:4860:4860::8888";Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("IPv6 agreement accepted",()=>{var o=Good();o.K.IP6="2001:4860:4860::8888";o.V6A=new Geo {Valid=true,IP=o.K.IP6,Zone=o.A.Zone,Country=o.A.Country};o.V6B=new Geo {Valid=true,IP=o.K.IP6,Zone=o.A.Zone,Country=o.A.Country};Assert(Policy.Evaluate(o,true).Zone==Zones.Berlin);});
   Test("IPv6 different region holds",()=>{var o=Good();o.K.IP6="2001:4860:4860::8888";o.V6A=new Geo {Valid=true,IP=o.K.IP6,Zone="Europe/Moscow",Country="RU"};o.V6B=o.V6A;Assert(Policy.Evaluate(o,true).Zone==null);});
   Test("Two samples required",()=>{var s=new Stability();var now=DateTime.UtcNow;Assert(!s.Accept(Policy.Evaluate(Good(),true),now).Ready);Assert(!s.Accept(Policy.Evaluate(Good(),true),now.AddSeconds(2.9)).Ready);Assert(s.Accept(Policy.Evaluate(Good(),true),now.AddSeconds(3)).Ready);});
   Test("Changed route resets candidate",()=>{var s=new Stability();var now=DateTime.UtcNow;s.Accept(Policy.Evaluate(Good(),true),now);var o=Good();o.Net.Key="new";Assert(!s.Accept(Policy.Evaluate(o,true),now.AddSeconds(3)).Ready);});
   Test("Failure breaks consecutive confirmations",()=>{var s=new Stability();var now=DateTime.UtcNow;s.Accept(Policy.Evaluate(Good(),true),now);var o=Good();o.A.Valid=false;s.Accept(Policy.Evaluate(o,true),now.AddSeconds(2.9));Assert(!s.Accept(Policy.Evaluate(Good(),true),now.AddSeconds(3)).Ready);});
   Test("Manual reset breaks stale candidate",()=>{var s=new Stability();var now=DateTime.UtcNow;s.Accept(Policy.Evaluate(Good(),true),now);s.Reset();Assert(!s.Accept(Policy.Evaluate(Good(),true),now.AddSeconds(3)).Ready);});
   Test("Kordon supported schema",()=>Assert(KordonState.FromJson("{\"kordon\":\"1.4.2\",\"pause\":false,\"status\":\"ok\",\"reason\":\"\",\"ip\":\"203.0.113.10\",\"country\":{\"code\":\"DE\"}}").Safe));
   Test("Kordon missing pause fails closed",()=>Assert(!KordonState.FromJson("{\"kordon\":\"1.4.2\",\"status\":\"ok\",\"ip\":\"203.0.113.10\",\"country\":{\"code\":\"DE\"}}").Safe));
   Test("Unknown Kordon version holds",()=>Assert(!KordonState.FromJson("{\"kordon\":\"2.0.0\",\"pause\":false,\"status\":\"ok\",\"ip\":\"203.0.113.10\"}").Safe));
   Test("Geo error JSON fails closed",()=>Assert(!Geo.Parse("{\"error\":true,\"reason\":\"RateLimited\"}",false).Valid));
   Test("Malformed geo fails closed",()=>Assert(!Geo.Parse("<html>error</html>",true).Valid));
   Test("Geo timezone field parse",()=>Assert(Geo.Parse("{\"success\":true,\"ip\":\"203.0.113.10\",\"country_code\":\"DE\",\"timezone\":{\"id\":\"Europe/Berlin\"}}",true).Valid));
   const string routes="0.0.0.0 0.0.0.0 192.168.0.1 192.168.0.130 25\n0.0.0.0 0.0.0.0 On-link 10.8.0.4 5\n203.0.113.10 255.255.255.255 192.168.0.1 192.168.0.130 24203\n192.168.0.0 255.255.255.0 On-link 192.168.0.130 281\n";
   Test("Actual route structure parsed",()=>{var r=NetworkState.Parse4(routes);Assert(r.Count==4);string selected;Assert(NetworkState.RouteProblem(r,"203.0.113.10",out selected)=="");Assert(selected=="10.8.0.4");});
   Test("Public route outside VPN holds",()=>{var r=NetworkState.Parse4(routes+"8.8.8.0 255.255.255.0 192.168.0.1 192.168.0.130 2\n");string selected;Assert(NetworkState.RouteProblem(r,"203.0.113.10",out selected)!="");});
   Test("Equal default routes hold",()=>{var r=NetworkState.Parse4(routes.Replace("0.130 25","0.130 5"));string selected;Assert(NetworkState.RouteProblem(r,"203.0.113.10",out selected)!="");});
   Test("Two /1 VPN routes accepted",()=>{var r=NetworkState.Parse4("0.0.0.0 0.0.0.0 192.168.0.1 192.168.0.130 1\n0.0.0.0 128.0.0.0 On-link 10.8.0.4 5\n128.0.0.0 128.0.0.0 On-link 10.8.0.4 5\n");string selected;Assert(NetworkState.RouteProblem(r,"203.0.113.10",out selected)=="");Assert(selected=="10.8.0.4");});
   Test("Missing routes hold",()=>{string selected;Assert(NetworkState.RouteProblem(new List<Route4>(),"",out selected)!="");});
   lines.Add("RESULT: "+(lines.Count-failed)+" passed, "+failed+" failed. Tests use fixtures; no VPN or system timezone changed.");File.WriteAllLines(path,lines,new UTF8Encoding(true));return failed==0?0:1;
  }
 }
}
