using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Linq;
using System.Windows.Forms;
using megaSpeedyAPI;

class Candle { public long time; public double open, high, low, close; public long volume; }
class TradeRecord { public long seq, receipt; public double price; public int qty; public string side,symbol; }
class Connector : Form {
 TextBox host=new TextBox(), port=new TextBox(), user=new TextBox(), password=new TextBox();
 Button login=new Button(), subscribe=new Button(), view=new Button(), phone=new Button();
 TextBox search=new TextBox(); CheckBox singleOnly=new CheckBox(); Label results=new Label(); readonly List<string> allContracts=new List<string>();
 ComboBox optionSeries=new ComboBox(); List<OptionDescriptor> optionCatalog=new List<OptionDescriptor>(); readonly OptionChain optionChain=new OptionChain(); string chosenOptionGroup=""; bool optionPlanQueued=false;
 ComboBox contracts=new ComboBox(); Label status=new Label(), last=new Label();
 spdQuoteAPI api; readonly object gate=new object(); readonly List<Candle> bars=new List<Candle>();
 readonly string feedId=Guid.NewGuid().ToString("N"); readonly List<TradeRecord> trades=new List<TradeRecord>(); long tradeSeq=0; bool selectedFutures=false;
 string symbol="", state="尚未登入", matchTime=""; bool authenticated=false, subscribed=false; long lastReceipt=0;
 long quoteReceipt=0, tradeCallbacks=0, selectedTrades=0, testTrades=0, bookCallbacks=0; double bid=0,ask=0; string lastTradeSymbol="";
 TcpListener server; bool closing=false, attempted=false, contractsReady=false; System.Windows.Forms.Timer loginTimer;
 readonly object phoneGate=new object(); MobileServer mobileServer; Process tunnelProcess; string phoneUrl="",phoneState="尚未啟動手機同步"; bool phoneStarting=false;
 int localPort=8765;
 [STAThread] static void Main() { Application.EnableVisualStyles(); Application.Run(new Connector()); }
 public Connector() {
  Text="兆豐行情連接 · 僅接收行情"; Width=620; Height=560; Font=new Font("Microsoft JhengHei",10);
  var labels=new string[]{"行情主機", "連接埠", "行情帳號", "行情密碼"}; var fields=new TextBox[]{host,port,user,password};
  for(int i=0;i<4;i++){Controls.Add(new Label(){Text=labels[i],Left=20,Top=24+i*40,Width=110});fields[i].SetBounds(140,20+i*40,420,28);Controls.Add(fields[i]);}
  password.UseSystemPasswordChar=true;
  login.Text="1. 登入行情";login.SetBounds(20,190,150,35);login.Click+=(s,e)=>Login();Controls.Add(login);
  Controls.Add(new Label(){Text="搜尋商品",Left=20,Top=239,Width=85});
  search.SetBounds(110,235,180,28);search.Text="TXF";search.TextChanged+=(sender,args)=>FilterContracts();Controls.Add(search);
  singleOnly.SetBounds(310,235,250,28);singleOnly.Text="隱藏跨月組合（含 /）";singleOnly.Checked=true;singleOnly.CheckedChanged+=(sender,args)=>FilterContracts();Controls.Add(singleOnly);
  results.SetBounds(20,270,540,25);results.Text="登入後可搜尋，例如 TXF、TXFJ6 或 TXX";Controls.Add(results);
  contracts.SetBounds(20,305,380,30);contracts.DropDownStyle=ComboBoxStyle.DropDownList;Controls.Add(contracts);
  subscribe.Text="2. 訂閱商品";subscribe.SetBounds(410,305,150,30);subscribe.Enabled=false;subscribe.Click+=(s,e)=>Subscribe();Controls.Add(subscribe);
  Controls.Add(new Label(){Text="選擇權組別",Left=20,Top=355,Width=110});optionSeries.SetBounds(140,350,420,30);optionSeries.DropDownStyle=ComboBoxStyle.DropDownList;optionSeries.Enabled=false;Controls.Add(optionSeries);
  status.SetBounds(20,405,560,48);status.Text=state;Controls.Add(status);
  last.SetBounds(20,465,560,40);last.Text="登入資料只保留於本次程式記憶體，不儲存檔案。";Controls.Add(last);
  view.Text="3. 開啟看盤";view.SetBounds(410,190,150,35);view.Enabled=false;view.Click+=(s,e)=>System.Diagnostics.Process.Start("http://127.0.0.1:"+localPort+"/?mode=live");Controls.Add(view);
  phone.Text="4. 手機外網同步";phone.SetBounds(185,190,210,35);phone.Enabled=false;phone.Click+=(sender,args)=>StartPhone();Controls.Add(phone);
  FormClosing+=(s,e)=>{closing=true;if(mobileServer!=null)mobileServer.Dispose();if(tunnelProcess!=null)try{if(!tunnelProcess.HasExited)tunnelProcess.Kill();}catch(Exception){}if(loginTimer!=null)loginTimer.Stop();if(server!=null)server.Stop();};
 }
 void UI(Action a){if(!closing&&!IsDisposed)try{BeginInvoke(a);}catch(InvalidOperationException){}}
 void SetState(string text){lock(gate)state=text;UI(()=>status.Text=text);}
 void FilterContracts(){
  if(subscribed)return;
  string previous=contracts.SelectedItem==null?null:contracts.SelectedItem.ToString();
  string query=search.Text.Trim();contracts.BeginUpdate();contracts.Items.Clear();
  foreach(string item in allContracts){string key=item.Substring(item.IndexOf(" | ")+3);if(singleOnly.Checked&&key.Contains("/"))continue;if(query.Length>0&&key.IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;contracts.Items.Add(item);}
  if(previous!=null&&contracts.Items.Contains(previous))contracts.SelectedItem=previous;else if(contracts.Items.Count>0)contracts.SelectedIndex=0;
  contracts.EndUpdate();subscribe.Enabled=authenticated&&contracts.Items.Count>0;
  results.Text=!contractsReady?"登入後可搜尋，例如 TXF、TXFJ6 或 TXX":contracts.Items.Count==0?"沒有符合的商品；此清單沒有找到您輸入的代碼。":"找到 "+contracts.Items.Count+" 個商品；請確認月份後選取，近月連續尚未提供。";
 }
 void Login(){if(attempted){Application.Restart();return;}int p;if(string.IsNullOrWhiteSpace(host.Text)||!int.TryParse(port.Text,out p)||p<1||p>65535||string.IsNullOrWhiteSpace(user.Text)||password.Text.Length==0){MessageBox.Show("請填入兆豐提供的行情主機、連接埠、帳號與密碼。");return;}
  attempted=true;login.Text="重新啟動連線";login.Enabled=false;
  string loginHost=host.Text.Trim(), loginUser=user.Text.Trim(), loginPassword=password.Text;
  host.Enabled=port.Enabled=user.Enabled=password.Enabled=false;
  SetState("正在連接兆豐行情主機，等待登入回覆…");
  loginTimer=new System.Windows.Forms.Timer();loginTimer.Interval=20000;
  loginTimer.Tick+=(sender,args)=>{loginTimer.Stop();if(!contractsReady){SetState("尚未完成連線。可稍候，或按重新啟動；請核對行情 IP、Port 與權限。");login.Enabled=true;}};loginTimer.Start();
  try{api=new spdQuoteAPI();
   api.evOnConnected=new OnQuoteConnected(()=>SetState("主機已連接，等待登入確認"));
   api.evOnDisconnected=new OnQuoteDisconnected(()=>{authenticated=false;SetState("已斷線：畫面保留最後資料，請關閉程式後重新登入");UI(()=>{subscribe.Enabled=false;login.Enabled=true;if(loginTimer!=null)loginTimer.Stop();});});
   api.evOnLogonResponse=new OnQuoteLogonResponse((ok,msg)=>{authenticated=ok;SetState(ok?"登入成功，等待商品清單":"登入遭拒。請核對行情帳密與 API 權限，再按重新啟動。");UI(()=>{if(ok){subscribe.Enabled=contractsReady&&contracts.Items.Count>0;optionSeries.Enabled=contractsReady;}else{login.Enabled=true;loginTimer.Stop();}});});
   api.evOnContractDownloadComplete=new OnQuoteContractDownloadComplete(n=>UI(()=>{contractsReady=true;loginTimer.Stop();allContracts.Clear();foreach(var key in api.Futures.Keys)allContracts.Add("期貨 | "+key);foreach(var key in api.Options.Keys)allContracts.Add("選擇權 | "+key);allContracts.Sort(StringComparer.OrdinalIgnoreCase);FilterContracts();optionCatalog.Clear();foreach(var pair in api.Options){var c=pair.Value;var descriptor=OptionDescriptor.Parse(pair.Key,c.MaturityDate,c.StrikePx,c.CallPut);if(descriptor!=null)optionCatalog.Add(descriptor);}optionSeries.Items.Clear();var today=DateTime.UtcNow.AddHours(8).Date;var groups=optionCatalog.GroupBy(c=>c.Group).OrderBy(g=>g.Min(c=>c.ExpiryDate)).ThenBy(g=>g.Key);foreach(var group in groups)optionSeries.Items.Add(group.Key);var preferred=optionCatalog.Where(c=>c.Prefix=="TXO"&&c.ExpiryDate>=today).OrderBy(c=>c.ExpiryDate).FirstOrDefault();if(preferred==null)preferred=optionCatalog.Where(c=>c.ExpiryDate>=today).OrderBy(c=>c.ExpiryDate).FirstOrDefault();if(preferred!=null)optionSeries.SelectedItem=preferred.Group;optionSeries.Enabled=authenticated;login.Enabled=true;SetState(authenticated?"登入成功，商品清單已下載。請選合約並按訂閱商品。":"商品清單已下載，仍在等待登入確認。");}));
   api.evOnTrade=new OnQuoteTrade(Trade);
   api.evOnOrderBook=new OnQuoteOrderBook(Book);
   StartServer();
   SetState("本機看盤已啟動（Port "+localPort+"），正在等待行情登入回覆…");
   password.Clear();
   var connection=new Thread(()=>{try{api.Logon(loginHost,p,loginUser,loginPassword,true);}catch(Exception ex){SetState("連線失敗："+ex.GetType().Name+"。可按重新啟動再試。");UI(()=>{loginTimer.Stop();login.Enabled=true;});}finally{loginPassword=null;}});connection.IsBackground=true;connection.Start();
  }catch(SocketException ex){loginTimer.Stop();login.Enabled=true;loginPassword=null;SetState("本機看盤服務啟動失敗："+ex.SocketErrorCode+"。請關閉舊工具後按重新啟動。");}
  catch(Exception ex){loginTimer.Stop();login.Enabled=true;loginPassword=null;SetState("無法啟動："+ex.GetType().Name+"。請確認官方套件與設定檔齊全，再按重新啟動。");}
 }
 void Subscribe(){if(!authenticated||contracts.SelectedItem==null||subscribed)return;var text=contracts.SelectedItem.ToString();var key=text.Substring(text.IndexOf(" | ")+3);chosenOptionGroup=optionSeries.SelectedItem==null?"":optionSeries.SelectedItem.ToString();optionPlanQueued=false;lock(gate){symbol=key;selectedFutures=text.StartsWith("期貨");bars.Clear();trades.Clear();lastReceipt=0;quoteReceipt=0;bid=0;ask=0;}try{bool accepted=text.StartsWith("期貨")?api.SubscribeContract(api.Futures[key]):api.SubscribeContract(api.Options[key]);if(!accepted){SetState("API 未接受商品訂閱，請核對行情權限與商品。");return;}subscribed=true;optionSeries.Enabled=false;subscribe.Enabled=false;contracts.Enabled=false;search.Enabled=false;singleOnly.Enabled=false;view.Enabled=true;phone.Enabled=true;SetState("已送出订閱，等待成交。切換商品請重新啟動程式。");}catch(Exception ex){SetState("訂閱失敗："+ex.GetType().Name);}}
 void QueueOptions(double reference){if(!selectedFutures||!Regex.IsMatch(symbol,@"^(TXF|MXF)[A-L][0-9]$")||chosenOptionGroup.Length==0||reference<=0)return;lock(gate){if(optionPlanQueued)return;optionPlanQueued=true;}UI(()=>{if(!subscribed)return;var plan=OptionChain.Select(optionCatalog.Where(c=>c.Group==chosenOptionGroup),reference);optionChain.Plan(chosenOptionGroup,plan);foreach(var c in plan){bool accepted=false;try{accepted=api.SubscribeContract(api.Options[c.Symbol]);}catch(Exception){}optionChain.Subscription(c.Symbol,accepted);}});}
 void Book(string exchange,string code,string time,MsgOrderBook book){code=(code??"").Trim();if(!book.IsTestMatch)optionChain.Book(code,book.BidPrice1,book.AskPrice1,book.BidQty1,book.AskQty1,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());lock(gate){bookCallbacks++;if(code.Trim()!=symbol||book.IsTestMatch)return;bid=book.BidPrice1;ask=book.AskPrice1;quoteReceipt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();if(lastReceipt==0)state="已收到真實委託報價，等待成交建立 K 線";}if(book.BidPrice1>0&&book.AskPrice1>0)QueueOptions((book.BidPrice1+book.AskPrice1)/2);}
 void Trade(string exchange,string code,string time,double price,int qty,bool test){code=(code??"").Trim();lock(gate){tradeCallbacks++;lastTradeSymbol=code;if(test)testTrades++;if(code.Trim()==symbol)selectedTrades++;}if(test||price<=0||double.IsNaN(price)||double.IsInfinity(price)||qty<=0)return;if(optionChain.Trade(code,price,qty,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())){lock(gate){trades.Add(new TradeRecord(){seq=++tradeSeq,receipt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),price=price,qty=qty,side="unknown",symbol=code});if(trades.Count>200)trades.RemoveAt(0);}return;}lock(gate){if(code.Trim()!=symbol)return;var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();string side="unknown";if(selectedFutures&&quoteReceipt>0&&now-quoteReceipt<=3000&&now>=quoteReceipt&&bid>0&&ask>bid){if(price>=ask)side="buy";else if(price<=bid)side="sell";}trades.Add(new TradeRecord(){seq=++tradeSeq,receipt=now,price=price,qty=qty,side=side,symbol=code});if(trades.Count>200)trades.RemoveAt(0);long minute=now/60000*60000;Candle c;if(bars.Count==0||bars[bars.Count-1].time!=minute){c=new Candle(){time=minute,open=price,high=price,low=price,close=price};bars.Add(c);if(bars.Count>300)bars.RemoveAt(0);}else c=bars[bars.Count-1];c.high=Math.Max(c.high,price);c.low=Math.Min(c.low,price);c.close=price;c.volume+=qty;lastReceipt=now;matchTime=time;state="已接收真實成交";}QueueOptions(price);UI(()=>{last.Text="商品 "+code+" ｜成交 "+price+" ｜單筆 "+qty+" ｜行情時間 "+time;status.Text="已接收真實成交";});}
 static string Escape(string s){var b=new StringBuilder();foreach(char c in s){if(c=='"'||c=='\\')b.Append('\\').Append(c);else if(c<32)b.Append("\\u").Append(((int)c).ToString("x4"));else b.Append(c);}return b.ToString();}
 string Snapshot(){lock(gate){var b=new StringBuilder();b.Append("{\"status\":\"").Append(Escape(state)).Append("\",\"symbol\":\"").Append(Escape(symbol)).Append("\",\"connected\":").Append(authenticated?"true":"false").Append(",\"lastReceipt\":").Append(lastReceipt).Append(",\"quoteReceipt\":").Append(quoteReceipt).Append(",\"bid\":").Append(bid.ToString(CultureInfo.InvariantCulture)).Append(",\"ask\":").Append(ask.ToString(CultureInfo.InvariantCulture)).Append(",\"tradeCallbacks\":").Append(tradeCallbacks).Append(",\"selectedTrades\":").Append(selectedTrades).Append(",\"testTrades\":").Append(testTrades).Append(",\"bookCallbacks\":").Append(bookCallbacks).Append(",\"lastTradeSymbol\":\"").Append(Escape(lastTradeSymbol)).Append("\"").Append(",\"matchTime\":\"").Append(Escape(matchTime)).Append("\",\"candles\":[");for(int i=0;i<bars.Count;i++){if(i>0)b.Append(',');var c=bars[i];b.Append("{\"time\":").Append(c.time).Append(",\"open\":").Append(c.open.ToString(CultureInfo.InvariantCulture)).Append(",\"high\":").Append(c.high.ToString(CultureInfo.InvariantCulture)).Append(",\"low\":").Append(c.low.ToString(CultureInfo.InvariantCulture)).Append(",\"close\":").Append(c.close.ToString(CultureInfo.InvariantCulture)).Append(",\"volume\":").Append(c.volume).Append('}');}b.Append("],\"feedId\":\"").Append(feedId).Append("\",\"tradeSeq\":").Append(tradeSeq).Append(",\"trades\":[");for(int i=0;i<trades.Count;i++){if(i>0)b.Append(',');var t=trades[i];b.Append("{\"seq\":").Append(t.seq).Append(",\"receipt\":").Append(t.receipt).Append(",\"price\":").Append(t.price.ToString(CultureInfo.InvariantCulture)).Append(",\"qty\":").Append(t.qty).Append(",\"side\":\"").Append(t.side).Append("\",\"symbol\":\"").Append(Escape(t.symbol??symbol)).Append("\"}");}return b.Append("],\"optionChain\":").Append(optionChain.Json()).Append("}").ToString();}}
 string PhoneSnapshot(){lock(phoneGate){long expiration=mobileServer==null?0:new DateTimeOffset(mobileServer.Expires).ToUnixTimeMilliseconds();return "{\"state\":\""+Escape(phoneState)+"\",\"url\":\""+Escape(phoneUrl)+"\",\"expires\":"+expiration+"}";}}
 void PhoneState(string text){lock(phoneGate)phoneState=text;UI(()=>phone.Text=phoneUrl.Length>0?"4. 手機配對 QR Code":"4. 手機外網同步");}
 void OpenPhonePage(){UI(()=>Process.Start("http://127.0.0.1:"+localPort+"/phone"));}
 static string FindCloudflared(){var candidates=new List<string>();string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);candidates.Add(Path.Combine(local,"Microsoft","WinGet","Links","cloudflared.exe"));string packages=Path.Combine(local,"Microsoft","WinGet","Packages");if(Directory.Exists(packages))foreach(string folder in Directory.GetDirectories(packages,"Cloudflare.cloudflared*"))foreach(string file in Directory.GetFiles(folder,"cloudflared.exe",SearchOption.AllDirectories))candidates.Add(file);string paths=Environment.GetEnvironmentVariable("PATH")??"";foreach(string folder in paths.Split(';')){if(folder.Length>0)try{candidates.Add(Path.Combine(folder.Trim('"'),"cloudflared.exe"));}catch(ArgumentException){}}foreach(string path in candidates)if(File.Exists(path))return path;return null;}
 bool TunnelAlive(){try{return tunnelProcess!=null&&!tunnelProcess.HasExited;}catch(InvalidOperationException){return false;}}
 void StartPhone(){if(phoneStarting)return;if(TunnelAlive()){OpenPhonePage();return;}phoneStarting=true;phone.Enabled=false;PhoneState("正在啟動 HTTPS 手機通道，首次可能需安裝 Cloudflare 工具…");
  var thread=new Thread(()=>{try{
   if(mobileServer!=null)mobileServer.Dispose();mobileServer=new MobileServer(Snapshot,Path.GetFullPath(".."));mobileServer.Start();lock(phoneGate)phoneUrl="";OpenPhonePage();
   string executable=FindCloudflared();if(executable==null){PhoneState("首次安裝 Cloudflare 工具，請查看安裝視窗。套件由 winget 驗證。");string script=Path.GetFullPath("Install-Tunnel.ps1");var installer=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+script+"\""){UseShellExecute=true});installer.WaitForExit();if(installer.ExitCode!=0)throw new InvalidOperationException("Cloudflare 安裝未完成。請檢查安裝視窗，必要時更新 Microsoft App Installer。");executable=FindCloudflared();if(executable==null)throw new InvalidOperationException("找不到 cloudflared.exe；請重新啟動工具後再試。");}
   if(closing){mobileServer.Dispose();return;}PhoneState("正在建立臨時 HTTPS 通道，請稍候…");var process=new Process();process.StartInfo=new ProcessStartInfo(executable,"tunnel --no-autoupdate --url http://127.0.0.1:"+mobileServer.Port){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};process.OutputDataReceived+=(sender,args)=>TunnelLine(args.Data);process.ErrorDataReceived+=(sender,args)=>TunnelLine(args.Data);process.EnableRaisingEvents=true;process.Exited+=(sender,args)=>{lock(phoneGate){phoneUrl="";phoneState="手機通道已關閉。請在電腦重新按手機外網同步。";}UI(()=>{phoneStarting=false;phone.Enabled=true;phone.Text="4. 手機外網同步";});};tunnelProcess=process;if(!process.Start())throw new IOException("無法啟動 Cloudflare 通道。");process.BeginOutputReadLine();process.BeginErrorReadLine();
  }catch(Exception ex){if(mobileServer!=null)mobileServer.Dispose();lock(phoneGate){phoneUrl="";phoneState="手機通道啟動失敗："+(ex is InvalidOperationException?ex.Message:ex.GetType().Name)+"。本機行情仍可使用。";}UI(()=>{phoneStarting=false;phone.Enabled=true;});}});thread.IsBackground=true;thread.Start();
 }
 void TunnelLine(string line){if(string.IsNullOrEmpty(line))return;var match=Regex.Match(line,@"https://[a-z0-9-]+\.trycloudflare\.com");if(!match.Success)return;lock(phoneGate){mobileServer.PublicHost=new Uri(match.Value).Host;phoneUrl=mobileServer.PairUrl(match.Value);phoneState="手機通道已建立，請用手機相機掃描 QR Code。";}UI(()=>{phoneStarting=false;phone.Enabled=true;phone.Text="4. 手機配對 QR Code";});}

 void StartServer(){
  bool started=false;
  for(int candidate=8765;candidate<=8785;candidate++){
   var listener=new TcpListener(IPAddress.Loopback,candidate);
   try{listener.Start();server=listener;localPort=candidate;started=true;break;}
   catch(SocketException ex){listener.Stop();if(ex.SocketErrorCode!=SocketError.AddressAlreadyInUse)throw;}
  }
  if(!started)throw new SocketException((int)SocketError.AddressAlreadyInUse);
var t=new Thread(()=>{while(!closing){try{using(var client=server.AcceptTcpClient()){client.ReceiveTimeout=2000;client.SendTimeout=2000;using(var stream=client.GetStream()){var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);string line=reader.ReadLine();int size=0;string h;while(!string.IsNullOrEmpty(h=reader.ReadLine())){size+=h.Length;if(size>8192)throw new IOException();}string body,kind,code;if(line=="GET /api/snapshot HTTP/1.1"){body=Snapshot();kind="application/json";code="200 OK";}else if(line=="GET /api/phone HTTP/1.1"){body=PhoneSnapshot();kind="application/json";code="200 OK";}else if(line=="GET /phone HTTP/1.1"){body=File.ReadAllText(Path.Combine("..","phone.html"),Encoding.UTF8);kind="text/html";code="200 OK";}else if(line=="GET /phone.js HTTP/1.1"||line=="GET /vendor/qrcode.js HTTP/1.1"){body=File.ReadAllText(Path.Combine("..",line.Split(' ')[1].Substring(1)),Encoding.UTF8);kind="text/javascript";code="200 OK";}else if(line=="GET /?mode=live HTTP/1.1"||line=="GET / HTTP/1.1"){body=File.ReadAllText(Path.Combine("..","index.html"),Encoding.UTF8).Replace("window.liveMode=new URLSearchParams(location.search).get(\"mode\")===\"live\";","window.liveMode=true;");kind="text/html";code="200 OK";}else if(line=="GET /large-order-alerts.js HTTP/1.1"||line=="GET /dashboard-live.js HTTP/1.1"){string asset=line.Split(' ')[1].Substring(1);body=File.ReadAllText(Path.Combine("..",asset),Encoding.UTF8);kind="text/javascript";code="200 OK";}else{body="Not found";kind="text/plain";code="404 Not Found";}var bytes=Encoding.UTF8.GetBytes(body);var header=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+"\r\nContent-Type: "+kind+"; charset=utf-8\r\nContent-Length: "+bytes.Length+"\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(bytes,0,bytes.Length);}}}catch(Exception){if(closing)break;}}});t.IsBackground=true;t.Start();}
}
