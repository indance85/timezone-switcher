using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace TimezoneSwitcher {
 public class Config {public bool Auto=false;public string ManualZone="";public bool Notifications=true;public string[] FavoriteZones=new[]{Zones.Moscow,Zones.Berlin};}
 public class Program {
  [STAThread] public static int Main(string[] args) {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   try {
    if(args.Length>0&&args[0]=="--self-test")return Tests.Run(args.Length>1?args[1]:"test-results.txt");
    if(args.Length>0&&args[0]=="--verify-auto")return VerifyAuto(args.Length>1?args[1]:"live-auto.json");
    if(args.Length>0&&(args[0]=="--diagnose"||args[0]=="--local-diagnose")) {
     Observation o;
     if(args[0]=="--local-diagnose") {o=new Observation();o.K=KordonState.Read().GetAwaiter().GetResult();o.Net=NetworkState.Read(o.K.IP);o.Error="Local-only diagnostic; geolocation not requested.";}
     else o=Probe.Read().GetAwaiter().GetResult();var d=Policy.Evaluate(o,true);
     File.WriteAllText(args.Length>1?args[1]:"diagnostics.txt","Kordon: "+o.K.Version+"\r\nAllowed: "+o.K.Safe+"\r\nCountry: "+o.K.Country+"\r\nRoutes: "+(o.Net.Problem==""?"consistent":o.Net.Problem)+"\r\nGeo A: "+o.A.Zone+" / "+o.A.Valid+"\r\nGeo B: "+o.B.Zone+" / "+o.B.Valid+"\r\nOffline: "+o.Offline+"\r\nChanged: "+o.Changed+"\r\nDecision: "+d.Reason+"\r\nCandidate: "+d.Zone+"\r\nRead only; no timezone changed.\r\n",new UTF8Encoding(true));return 0;
    }
    bool smoke=args.Length>0&&args[0]=="--smoke-test";bool created;
    using(var mutex=new Mutex(true,@"Local\TimezoneSwitcher.WPF.1"+(smoke?".test":""),out created)) {
     if(!created) {try{using(var ev=EventWaitHandle.OpenExisting(@"Local\TimezoneSwitcher.Show.1"))ev.Set();}catch{}return 0;}
     try {var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};var host=new Host(app,smoke,args.Length>1?args[1]:"",args.Contains("--tray"));app.Run();return host.ExitCode;}finally{mutex.ReleaseMutex();}
    }
   }catch(Exception e){Forms.MessageBox.Show("Не удалось запустить Timezone Switcher.\r\n"+e.Message,"Timezone Switcher");return 1;}
  }
  static int VerifyAuto(string path) {
   // Read-only entry point: no UI Host is created and no timezone setter is called.
   TimeZoneInfo.ClearCachedData();string before=TimeZoneInfo.Local.Id;
   var stability=new Stability();var samples=new System.Collections.Generic.List<object>();bool firstReady=false,lastReady=false,manualWouldChange=false;
   for(int i=0;i<2;i++) {
    if(i>0)Thread.Sleep(3000);
    var o=Probe.Read().GetAwaiter().GetResult();var d=stability.Accept(Policy.Evaluate(o,true),DateTime.UtcNow);
    var manual=Policy.Evaluate(o,false);manualWouldChange=manual.Zone!=null;
    if(i==0)firstReady=d.Ready;lastReady=d.Ready;
    samples.Add(new {observedAtUtc=DateTime.UtcNow.ToString("o"),kordonVersion=o.K.Version,kordonAllowed=o.K.Safe,country=o.K.Country,ipv6ReportedByKordon=o.K.IP6!="",routesProblem=o.Net.Problem,networkChangedDuringProbe=o.Changed,offline=o.Offline,ipwhoIsValid=o.A.Valid,ipwhoIsTimezone=o.A.Zone,ipapiCoValid=o.B.Valid,ipapiCoTimezone=o.B.Zone,bothIPsMatchKordon=o.A.Valid&&o.B.Valid&&o.A.IP==o.K.IP&&o.B.IP==o.K.IP,candidate=d.Zone,readyToApply=d.Ready,reason=d.Reason,manualWouldChangeTimezone=manualWouldChange});
   }
   TimeZoneInfo.ClearCachedData();string after=TimeZoneInfo.Local.Id;
   bool passed=!firstReady&&lastReady&&!manualWouldChange&&before==after;
   var report=new {readOnly=true,liveAutoConfirmationPassed=passed,timezoneBefore=before,timezoneAfter=after,samples=samples,limitations="No timezone setter invoked. No VPN toggling or outage induced. IPv6 only tested when Kordon reports it."};
   File.WriteAllText(path,new JavaScriptSerializer().Serialize(report),new UTF8Encoding(true));return passed?0:2;
  }
 }
 public class Host {
  Application app;Window window;Config config;Forms.NotifyIcon tray;System.Drawing.Icon trayIcon;DispatcherTimer clock,poll;EventWaitHandle showEvent;
  Forms.ToolStripMenuItem menuAuto,menuMoscow,menuBerlin,menuFavorites;
  ComboBox favorites;
  bool test,busy,quitting;int generation;int transitionRetries=3;DateTime next=DateTime.MinValue;Stability stable=new Stability();string dir,logPath;public int ExitCode;
  string lastApplied="",lastStatus="";int conflicts;int testChanges;
  Brush green=(Brush)new BrushConverter().ConvertFromString("#D1EBDE");Brush gray=(Brush)new BrushConverter().ConvertFromString("#E9ECF0");
  public Host(Application application,bool smoke,string target,bool hidden) {
   app=application;test=smoke;
   dir=test?Path.GetFullPath(target):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TimezoneSwitcher");Directory.CreateDirectory(dir);logPath=Path.Combine(dir,"activity.log");
   config=Load();
   using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("Main.xaml"))window=(Window)XamlReader.Load(s);
   using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("timezone-icon.png"))window.Icon=BitmapFrame.Create(s,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
   window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!quitting){e.Cancel=true;window.Hide();}};
   window.StateChanged+=delegate{if(window.WindowState==WindowState.Minimized){window.Hide();window.WindowState=WindowState.Normal;}};
   B("HideButton").Click+=delegate{window.Hide();};
   B("ManualButton").Click+=delegate{SetMode(false);};B("AutoButton").Click+=delegate{SetMode(true);};
   B("MoscowButton").Click+=delegate{Choose(Zones.Moscow);};B("BerlinButton").Click+=delegate{Choose(Zones.Berlin);};
   B("SettingsButton").Click+=delegate{Settings();};B("CheckButton").Click+=delegate{next=DateTime.MinValue;Check();};
   favorites=(ComboBox)window.FindName("FavoriteZones");
   B("AddZoneButton").Click+=delegate{ZonePicker();};
   B("ApplyZoneButton").Click+=delegate{if(favorites.SelectedValue!=null)Choose((string)favorites.SelectedValue);};
   B("RemoveZoneButton").Click+=delegate{if(favorites.SelectedValue!=null){var id=(string)favorites.SelectedValue;config.FavoriteZones=config.FavoriteZones.Where(x=>x!=id).ToArray();Save();RefreshFavorites();}};
   var menu=new Forms.ContextMenuStrip();menu.Items.Add("Открыть часы",null,delegate{Show();});
   menuMoscow=(Forms.ToolStripMenuItem)menu.Items.Add("Москва",null,delegate{Choose(Zones.Moscow);});menuBerlin=(Forms.ToolStripMenuItem)menu.Items.Add("Берлин",null,delegate{Choose(Zones.Berlin);});menu.Items.Add(new Forms.ToolStripSeparator());
   menuFavorites=(Forms.ToolStripMenuItem)menu.Items.Add("Мои часовые пояса");RefreshFavorites();
   menuAuto=(Forms.ToolStripMenuItem)menu.Items.Add("Авто · Kordon",null,delegate{SetMode(!config.Auto);});menu.Items.Add("Проверить сейчас",null,delegate{next=DateTime.MinValue;Check();});menu.Items.Add("Настройки",null,delegate{Show();Settings();});menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Выход",null,delegate{Quit();});
   using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("timezone.ico"))
   using(var icon=new System.Drawing.Icon(s,Forms.SystemInformation.SmallIconSize))trayIcon=(System.Drawing.Icon)icon.Clone();
   tray=new Forms.NotifyIcon {Icon=trayIcon,Text="Timezone Switcher",ContextMenuStrip=menu,Visible=true};
   tray.MouseDoubleClick+=delegate(object sender,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)Show();};
   if(!test)showEvent=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\TimezoneSwitcher.Show.1");
   clock=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};clock.Tick+=delegate{UpdateClocks();if(showEvent!=null&&showEvent.WaitOne(0))Show();};clock.Start();
   poll=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};poll.Tick+=delegate{if(config.Auto&&DateTime.UtcNow>=next)Check();};if(!test)poll.Start();
   if(!test) {NetworkChange.NetworkAddressChanged+=NetworkChanged;NetworkChange.NetworkAvailabilityChanged+=AvailabilityChanged;SystemEvents.PowerModeChanged+=PowerChanged;}
   UpdateMode();UpdateClocks();
   if(!hidden||test)window.Show();
   if(test){app.Dispatcher.BeginInvoke(new Action(Smoke),DispatcherPriority.ApplicationIdle);}
   else {if(!config.Auto&&!String.IsNullOrEmpty(config.ManualZone))Apply(config.ManualZone,"Сохранённый ручной выбор");if(config.Auto)Check();}
  }
  Button B(string name){return (Button)window.FindName(name);}TextBlock T(string name){return (TextBlock)window.FindName(name);}
  void Log(string text) {try{if(File.Exists(logPath)&&new FileInfo(logPath).Length>262144)File.WriteAllText(logPath,"");File.AppendAllText(logPath,DateTime.UtcNow.ToString("o")+" "+text+Environment.NewLine,Encoding.UTF8);}catch{}}
  Config Load() {try {var path=Path.Combine(dir,"settings.json");if(!File.Exists(path))return new Config();var c=new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(path,Encoding.UTF8));if(c==null)throw new IOException();if(!ZoneCatalog.Valid(c.ManualZone))c.ManualZone="";c.FavoriteZones=ZoneCatalog.Normalize(c.FavoriteZones);return c;}catch{Log("Settings unreadable; starting in Manual.");return new Config();}}
  bool Save() {try {var p=Path.Combine(dir,"settings.json");File.WriteAllText(p+".tmp",new JavaScriptSerializer().Serialize(config),new UTF8Encoding(true));if(File.Exists(p))File.Replace(p+".tmp",p,null);else File.Move(p+".tmp",p);return true;}catch(Exception e){Status("Настройки не сохранены",e.Message);return false;}}
  void Show(){window.Show();window.WindowState=WindowState.Normal;window.Activate();}
  void Status(string title,string detail){T("StatusTitle").Text=title;T("StatusDetail").Text=detail;if(lastStatus!=title+detail){Log(title+": "+detail);lastStatus=title+detail;}}
  void SetMode(bool auto) {
   generation++;stable.Reset();transitionRetries=3;lastApplied="";conflicts=0;config.Auto=auto;if(auto)config.ManualZone="";Save();UpdateMode();next=DateTime.MinValue;
   if(auto&&!test)Check();
  }
  void UpdateMode() {
   ((FrameworkElement)window.FindName("ManualZonesPanel")).Visibility=config.Auto?Visibility.Collapsed:Visibility.Visible;
   B("AutoButton").Background=config.Auto?green:gray;B("ManualButton").Background=config.Auto?gray:green;menuAuto.Checked=config.Auto;
   if(config.Auto)Status("Авто · Kordon","Проверяем выход в интернет и часовой пояс региона.");else Status("Ручной режим","Выбор города действует до включения Авто.");
  }
  void Choose(string zone){if(!ZoneCatalog.Valid(zone))return;SetMode(false);config.ManualZone=zone;Save();Apply(zone,"Ручной выбор");}
  string Name(string id){return id==Zones.Moscow?"Москва":id==Zones.Berlin?"Берлин":ZoneCatalog.Name(id);}
  void RefreshFavorites() {
   string selected=favorites.SelectedValue as string;
   favorites.ItemsSource=config.FavoriteZones.Select(id=>new ZoneOption(id)).ToArray();
   favorites.DisplayMemberPath="Label";favorites.SelectedValuePath="Id";
   favorites.SelectedValue=selected??config.ManualZone;
   if(favorites.SelectedIndex<0&&favorites.Items.Count>0)favorites.SelectedIndex=0;
   B("ApplyZoneButton").IsEnabled=favorites.Items.Count>0;B("RemoveZoneButton").IsEnabled=favorites.Items.Count>0;
   if(menuFavorites!=null){menuFavorites.DropDownItems.Clear();foreach(string zone in config.FavoriteZones){string id=zone;menuFavorites.DropDownItems.Add(Name(id),null,delegate{Choose(id);});}menuFavorites.Enabled=config.FavoriteZones.Length>0;}
  }
  void AddFavorite(string id) {
   if(!ZoneCatalog.Valid(id))return;
   config.FavoriteZones=ZoneCatalog.Normalize(config.FavoriteZones.Concat(new[]{id}).ToArray());Save();RefreshFavorites();favorites.SelectedValue=id;
  }
  void ZonePicker() {
   var w=new Window{Title="Добавить часовой пояс",Owner=window,Width=540,Height=510,MinWidth=400,MinHeight=360,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=window.Background,FontFamily=window.FontFamily};
   var panel=new DockPanel{Margin=new Thickness(20)};w.Content=panel;
   var hint=new TextBlock{Text="Выберите регион: в одной стране может быть несколько поясов.",Margin=new Thickness(0,0,0,12),TextWrapping=TextWrapping.Wrap};DockPanel.SetDock(hint,Dock.Top);panel.Children.Add(hint);
   var label=new TextBlock{Text="Поиск страны, города или часового пояса",Margin=new Thickness(0,0,0,5)};DockPanel.SetDock(label,Dock.Top);panel.Children.Add(label);
   var search=new TextBox{Padding=new Thickness(8),Margin=new Thickness(0,0,0,12)};System.Windows.Automation.AutomationProperties.SetName(search,"Поиск страны, города или часового пояса");DockPanel.SetDock(search,Dock.Top);panel.Children.Add(search);
   var add=new Button{Content="Добавить в мои пояса",Padding=new Thickness(12,10,12,10),Margin=new Thickness(0,12,0,0),IsEnabled=false};DockPanel.SetDock(add,Dock.Bottom);panel.Children.Add(add);
   var list=new ListBox{DisplayMemberPath="Label",HorizontalContentAlignment=HorizontalAlignment.Stretch};ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);panel.Children.Add(list);
   var options=TimeZoneInfo.GetSystemTimeZones().Select(z=>new ZoneOption(z.Id)).ToArray();
   Action filter=delegate{string q=search.Text.Trim();list.ItemsSource=options.Where(z=>z.Search.IndexOf(q,StringComparison.CurrentCultureIgnoreCase)>=0).ToArray();};
   search.TextChanged+=delegate{filter();};list.SelectionChanged+=delegate{add.IsEnabled=list.SelectedItem!=null;};
   add.Click+=delegate{var option=list.SelectedItem as ZoneOption;if(option!=null){AddFavorite(option.Id);w.Close();}};
   filter();w.Loaded+=delegate{search.Focus();};w.ShowDialog();
  }
  void UpdateClocks() {
   TimeZoneInfo.ClearCachedData();var current=TimeZoneInfo.Local;var now=DateTime.UtcNow;var culture=CultureInfo.GetCultureInfo("ru-RU");
   T("ActiveZone").Text=Name(current.Id);T("SystemClock").Text=TimeZoneInfo.ConvertTimeFromUtc(now,current).ToString("HH:mm:ss");T("SystemDate").Text=TimeZoneInfo.ConvertTimeFromUtc(now,current).ToString("dddd, d MMMM",culture);
   foreach(var pair in new[]{new[]{"Moscow",Zones.Moscow},new[]{"Berlin",Zones.Berlin}}){var z=TimeZoneInfo.FindSystemTimeZoneById(pair[1]);T(pair[0]+"Clock").Text=TimeZoneInfo.ConvertTimeFromUtc(now,z).ToString("HH:mm");var offset=z.GetUtcOffset(now);T(pair[0]+"Offset").Text="UTC"+(offset.Ticks>=0?"+":"−")+offset.Duration().ToString(@"h\:mm")+(current.Id==z.Id?" · активен":"");B(pair[0]+"Button").BorderBrush=current.Id==z.Id?green:Brushes.Transparent;}
   menuMoscow.Text="Москва · "+T("MoscowClock").Text;menuBerlin.Text="Берлин · "+T("BerlinClock").Text;menuMoscow.Checked=current.Id==Zones.Moscow;menuBerlin.Checked=current.Id==Zones.Berlin;
   var tip="Timezone · "+Name(current.Id);tray.Text=tip.Substring(0,Math.Min(63,tip.Length));
  }
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr h,uint m,IntPtr w,string l,uint flags,uint timeout,out IntPtr result);
  bool Apply(string zone,string reason) {
   if(test){testChanges++;Status("Тестовый режим",Name(zone)+" · системный пояс не изменён");return true;}
   try {
    TimeZoneInfo.FindSystemTimeZoneById(zone);TimeZoneInfo.ClearCachedData();if(TimeZoneInfo.Local.Id==zone){Status(config.Auto?"Авто · пояс подтверждён":"Ручной режим",Name(zone)+" · "+reason);return true;}
    var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"tzutil.exe"),"/s \""+zone+"\""){UseShellExecute=false,CreateNoWindow=true};
    using(var p=Process.Start(start)){if(!p.WaitForExit(5000)){p.Kill();throw new IOException("Windows не ответила вовремя");}if(p.ExitCode!=0)throw new UnauthorizedAccessException("Windows не разрешила изменить часовой пояс. Проверьте права пользователя.");}
    TimeZoneInfo.ClearCachedData();if(TimeZoneInfo.Local.Id!=zone)throw new IOException("Windows не подтвердила изменение часового пояса");
    IntPtr result;SendMessageTimeout(new IntPtr(0xffff),0x001A,IntPtr.Zero,"TimeZoneInformation",0x0002|0x0020,1000,out result);
    lastApplied=zone;UpdateClocks();Status("Часовой пояс изменён",Name(zone)+" · "+reason);
    if(config.Notifications)tray.ShowBalloonTip(4000,"Timezone Switcher", "Часовой пояс: "+Name(zone)+". "+reason,Forms.ToolTipIcon.Info);
    return true;
   }catch(Exception e){Status("Не удалось изменить пояс",e.Message);if(config.Notifications)tray.ShowBalloonTip(5000,"Timezone Switcher",e.Message,Forms.ToolTipIcon.Warning);return false;}
  }
  async void Check() {
   if(busy||test||quitting)return;
   busy=true;int epoch=generation;B("CheckButton").IsEnabled=false;next=DateTime.UtcNow.AddMinutes(2);Status("Проверка сети","Читаем Kordon и сверяем регион у двух сервисов.");
   try {
    var o=await Probe.Read();if(quitting||epoch!=generation)return;
    var d=Policy.Evaluate(o,config.Auto);
    if(!config.Auto){Status("Ручной режим · диагностика",o.K.Available?"Kordon "+o.K.Version+" · "+o.K.Country+". Ваш выбор сохранён.":o.K.Reason);return;}
    d=stable.Accept(d,DateTime.UtcNow);
    if(d.Zone==null){if(transitionRetries>0){transitionRetries--;next=DateTime.UtcNow.AddSeconds(3);Status("Авто · повтор через 3 с",d.Reason);}else Status("Авто · ожидает",d.Reason);return;}
    if(!d.Ready){Status("Повторная проверка через 3 с",d.Reason);next=DateTime.UtcNow.AddSeconds(3);return;}
    TimeZoneInfo.ClearCachedData();
    if(lastApplied!=""&&TimeZoneInfo.Local.Id!=lastApplied){conflicts++;lastApplied="";if(conflicts>=2){SetMode(false);Status("Авто приостановлен","Другая программа меняет часовой пояс. Проверьте настройки времени Windows.");return;}}
    Apply(d.Zone,d.Reason);
   }catch(Exception e){stable.Reset();Status("Проверка не завершена",e.Message);}
   finally{busy=false;B("CheckButton").IsEnabled=true;}
  }
  void Invalidate(){if(quitting)return;app.Dispatcher.BeginInvoke(new Action(delegate{generation++;stable.Reset();transitionRetries=3;next=DateTime.UtcNow.AddMilliseconds(500);}));}
  void NetworkChanged(object s,EventArgs e){Invalidate();}void AvailabilityChanged(object s,NetworkAvailabilityEventArgs e){Invalidate();}void PowerChanged(object s,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Resume)Invalidate();}
  bool StartupEnabled(){using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return k!=null&&k.GetValue("TimezoneSwitcher")!=null;}
  void Startup(bool enabled) {
   if(test)return;
   using(var k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
    if(enabled)k.SetValue("TimezoneSwitcher","\""+Assembly.GetExecutingAssembly().Location+"\" --tray",RegistryValueKind.String);else k.DeleteValue("TimezoneSwitcher",false);
   }
  }
  void Settings() {
   var w=new Window {Title="Настройки",Owner=window,Width=390,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=window.Background,FontFamily=window.FontFamily};
   var panel=new StackPanel {Margin=new Thickness(22)};w.Content=panel;
   var startup=new CheckBox {Content="Запускать при входе в Windows",IsChecked=StartupEnabled(),Margin=new Thickness(0,0,0,15)};
   var notifications=new CheckBox {Content="Уведомлять о смене пояса",IsChecked=config.Notifications,Margin=new Thickness(0,0,0,18)};panel.Children.Add(startup);panel.Children.Add(notifications);
   panel.Children.Add(new TextBlock {Text="Авто: Kordon → два сервиса геолокации → повторная проверка. Нет интернета: Москва. Неоднозначный маршрут: сохранить пояс.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
   panel.Children.Add(new TextBlock {Text="Проверка обычно раз в 2 минуты и после смены сети. IP передаётся ipwho.is и ipapi.co. При системном прокси/PAC Авто ожидает. Для общего часового пояса используйте полный VPN-туннель.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,12)});
   panel.Children.Add(new TextBlock {Text="Автоматическое определение часового пояса в настройках Windows лучше выключить. Летнее время Берлина учитывается автоматически.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,0,0,18)});
   var save=new Button {Content="Сохранить",Padding=new Thickness(12,8,12,8),IsDefault=true};panel.Children.Add(save);
   save.Click+=delegate{try{Startup(startup.IsChecked==true);config.Notifications=notifications.IsChecked==true;if(Save())w.Close();}catch(Exception e){MessageBox.Show(w,e.Message,"Не удалось сохранить");}};
   w.ShowDialog();
  }
  void Render(string name) {
   window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(dir,name)))encoder.Save(file);
  }
  void Smoke() {
   try {
    B("BerlinButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(config.Auto||config.ManualZone!=Zones.Berlin)throw new Exception("Manual Berlin click failed");
    B("MoscowButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(config.ManualZone!=Zones.Moscow||Load().ManualZone!=Zones.Moscow)throw new Exception("Manual Moscow click or persistence failed");
    AddFavorite("Tokyo Standard Time");AddFavorite("India Standard Time");AddFavorite("Tokyo Standard Time");
    if(config.FavoriteZones.Count(x=>x=="Tokyo Standard Time")!=1)throw new Exception("Duplicate favorite");
    favorites.SelectedValue="India Standard Time";B("ApplyZoneButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if(config.Auto||Load().ManualZone!="India Standard Time"||!Load().FavoriteZones.Contains("Tokyo Standard Time"))throw new Exception("Custom manual persistence");
    B("RemoveZoneButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(config.FavoriteZones.Contains("India Standard Time")||config.ManualZone!="India Standard Time")throw new Exception("Remove must not change active choice");
    B("AutoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(!config.Auto)throw new Exception("Auto mode failed");
    B("ManualButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(config.Auto)throw new Exception("Manual mode failed");
    window.Close();if(window.IsVisible)throw new Exception("Close-to-tray failed");Show();if(!window.IsVisible)throw new Exception("Restore failed");
    Status("Ручной режим","Выберите город или включите Авто с проверкой Kordon.");UpdateClocks();Render("window.png");
    File.WriteAllText(Path.Combine(dir,"ui-tests.txt"),"PASS: WPF load, Cyrillic, city buttons, Auto/Manual, settings persistence, custom zone apply, multiple favorites, deduplication, removal without timezone change, close-to-tray, restore, clock rendering.\r\nSimulated changes: "+testChanges+". No real timezone or startup changes.\r\n",Encoding.UTF8);
   }catch(Exception e){ExitCode=1;File.WriteAllText(Path.Combine(dir,"ui-tests.txt"),e.ToString());}finally{Quit();}
  }
  void Quit() {
   if(quitting)return;quitting=true;generation++;clock.Stop();poll.Stop();tray.Visible=false;tray.Dispose();trayIcon.Dispose();if(showEvent!=null)showEvent.Dispose();
   if(!test){NetworkChange.NetworkAddressChanged-=NetworkChanged;NetworkChange.NetworkAvailabilityChanged-=AvailabilityChanged;SystemEvents.PowerModeChanged-=PowerChanged;}
   window.Close();app.Shutdown();
  }
 }
}
