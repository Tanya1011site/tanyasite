using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using megaSpeedyAPI;

class Candle { public long time; public double open, high, low, close; public long volume; }
class Connector : Form {
 TextBox host=new TextBox(), port=new TextBox(), user=new TextBox(), password=new TextBox();
 Button login=new Button(), subscribe=new Button(), view=new Button();
 TextBox search=new TextBox(); CheckBox singleOnly=new CheckBox(); Label results=new Label(); readonly List<string> allContracts=new List<string>();
 ComboBox contracts=new ComboBox(); Label status=new Label(), last=new Label();
 spdQuoteAPI api; readonly object gate=new object(); readonly List<Candle> bars=new List<Candle>();
 string symbol="", state="尚未登入", matchTime=""; bool authenticated=false, subscribed=false; long lastReceipt=0;
 long quoteReceipt=0, tradeCallbacks=0, selectedTrades=0, testTrades=0, bookCallbacks=0; double bid=0,ask=0; string lastTradeSymbol="";
 TcpListener server; bool closing=false, attempted=false, contractsReady=false; System.Windows.Forms.Timer loginTimer;
 int localPort=8765;
 [STAThread] static void Main() { Application.EnableVisualStyles(); Application.Run(new Connector()); }
 public Connector() {
  Text="兆豐行情連接 · 僅接收行情"; Width=620; Height=505; Font=new Font("Microsoft JhengHei",10);
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
  status.SetBounds(20,350,560,48);status.Text=state;Controls.Add(status);
  last.SetBounds(20,408,560,40);last.Text="登入資料只保留於本次程式記憶體，不儲存檔案。";Controls.Add(last);
  view.Text="3. 開啟看盤";view.SetBounds(410,190,150,35);view.Enabled=false;view.Click+=(s,e)=>System.Diagnostics.Process.Start("http://127.0.0.1:"+localPort+"/");Controls.Add(view);
  FormClosing+=(s,e)=>{closing=true;if(loginTimer!=null)loginTimer.Stop();if(server!=null)server.Stop();};
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
   api.evOnLogonResponse=new OnQuoteLogonResponse((ok,msg)=>{authenticated=ok;SetState(ok?"登入成功，等待商品清單":"登入遭拒。請核對行情帳密與 API 權限，再按重新啟動。");UI(()=>{if(ok){subscribe.Enabled=contractsReady&&contracts.Items.Count>0;}else{login.Enabled=true;loginTimer.Stop();}});});
   api.evOnContractDownloadComplete=new OnQuoteContractDownloadComplete(n=>UI(()=>{contractsReady=true;loginTimer.Stop();allContracts.Clear();foreach(var key in api.Futures.Keys)allContracts.Add("期貨 | "+key);foreach(var key in api.Options.Keys)allContracts.Add("選擇權 | "+key);allContracts.Sort(StringComparer.OrdinalIgnoreCase);FilterContracts();login.Enabled=true;SetState(authenticated?"登入成功，商品清單已下載。請選合約並按訂閱商品。":"商品清單已下載，仍在等待登入確認。");}));
   api.evOnTrade=new OnQuoteTrade(Trade);
   api.evOnOrderBook=new OnQuoteOrderBook(Book);
   StartServer();
   SetState("本機看盤已啟動（Port "+localPort+"），正在等待行情登入回覆…");
   password.Clear();
   var connection=new Thread(()=>{try{api.Logon(loginHost,p,loginUser,loginPassword,true);}catch(Exception ex){SetState("連線失敗："+ex.GetType().Name+"。可按重新啟動再試。");UI(()=>{loginTimer.Stop();login.Enabled=true;});}finally{loginPassword=null;}});connection.IsBackground=true;connection.Start();
  }catch(SocketException ex){loginTimer.Stop();login.Enabled=true;loginPassword=null;SetState("本機看盤服務啟動失敗："+ex.SocketErrorCode+"。請關閉舊工具後按重新啟動。");}
  catch(Exception ex){loginTimer.Stop();login.Enabled=true;loginPassword=null;SetState("無法啟動："+ex.GetType().Name+"。請確認官方套件與設定檔齊全，再按重新啟動。");}
 }
 void Subscribe(){if(!authenticated||contracts.SelectedItem==null||subscribed)return;var text=contracts.SelectedItem.ToString();var key=text.Substring(text.IndexOf(" | ")+3);lock(gate){symbol=key;bars.Clear();lastReceipt=0;}try{if(text.StartsWith("期貨"))api.SubscribeContract(api.Futures[key]);else api.SubscribeContract(api.Options[key]);subscribed=true;subscribe.Enabled=false;contracts.Enabled=false;search.Enabled=false;singleOnly.Enabled=false;view.Enabled=true;SetState("已送出订閱，等待成交。切換商品請重新啟動程式。");}catch(Exception ex){SetState("訂閱失敗："+ex.GetType().Name);}}
 void Book(string exchange,string code,string time,MsgOrderBook book){lock(gate){bookCallbacks++;if(code.Trim()!=symbol||book.IsTestMatch)return;bid=book.BidPrice1;ask=book.AskPrice1;quoteReceipt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();if(lastReceipt==0)state="已收到真實委託報價，等待成交建立 K 線";}}
 void Trade(string exchange,string code,string time,double price,int qty,bool test){lock(gate){tradeCallbacks++;lastTradeSymbol=code;if(test)testTrades++;if(code.Trim()==symbol)selectedTrades++;}if(test||price<=0||double.IsNaN(price)||double.IsInfinity(price)||qty<=0)return;lock(gate){if(code.Trim()!=symbol)return;var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();long minute=now/60000*60000;Candle c;if(bars.Count==0||bars[bars.Count-1].time!=minute){c=new Candle(){time=minute,open=price,high=price,low=price,close=price};bars.Add(c);if(bars.Count>300)bars.RemoveAt(0);}else c=bars[bars.Count-1];c.high=Math.Max(c.high,price);c.low=Math.Min(c.low,price);c.close=price;c.volume+=qty;lastReceipt=now;matchTime=time;state="已接收真實成交";}UI(()=>{last.Text="商品 "+code+" ｜成交 "+price+" ｜單筆 "+qty+" ｜行情時間 "+time;status.Text="已接收真實成交";});}
 static string Escape(string s){var b=new StringBuilder();foreach(char c in s){if(c=='"'||c=='\\')b.Append('\\').Append(c);else if(c<32)b.Append("\\u").Append(((int)c).ToString("x4"));else b.Append(c);}return b.ToString();}
 string Snapshot(){lock(gate){var b=new StringBuilder();b.Append("{\"status\":\"").Append(Escape(state)).Append("\",\"symbol\":\"").Append(Escape(symbol)).Append("\",\"connected\":").Append(authenticated?"true":"false").Append(",\"lastReceipt\":").Append(lastReceipt).Append(",\"quoteReceipt\":").Append(quoteReceipt).Append(",\"bid\":").Append(bid.ToString(CultureInfo.InvariantCulture)).Append(",\"ask\":").Append(ask.ToString(CultureInfo.InvariantCulture)).Append(",\"tradeCallbacks\":").Append(tradeCallbacks).Append(",\"selectedTrades\":").Append(selectedTrades).Append(",\"testTrades\":").Append(testTrades).Append(",\"bookCallbacks\":").Append(bookCallbacks).Append(",\"lastTradeSymbol\":\"").Append(Escape(lastTradeSymbol)).Append("\"").Append(",\"matchTime\":\"").Append(Escape(matchTime)).Append("\",\"candles\":[");for(int i=0;i<bars.Count;i++){if(i>0)b.Append(',');var c=bars[i];b.Append("{\"time\":").Append(c.time).Append(",\"open\":").Append(c.open.ToString(CultureInfo.InvariantCulture)).Append(",\"high\":").Append(c.high.ToString(CultureInfo.InvariantCulture)).Append(",\"low\":").Append(c.low.ToString(CultureInfo.InvariantCulture)).Append(",\"close\":").Append(c.close.ToString(CultureInfo.InvariantCulture)).Append(",\"volume\":").Append(c.volume).Append('}');}return b.Append("]}").ToString();}}
 void StartServer(){
  bool started=false;
  for(int candidate=8765;candidate<=8785;candidate++){
   var listener=new TcpListener(IPAddress.Loopback,candidate);
   try{listener.Start();server=listener;localPort=candidate;started=true;break;}
   catch(SocketException ex){listener.Stop();if(ex.SocketErrorCode!=SocketError.AddressAlreadyInUse)throw;}
  }
  if(!started)throw new SocketException((int)SocketError.AddressAlreadyInUse);
var t=new Thread(()=>{while(!closing){try{using(var client=server.AcceptTcpClient()){client.ReceiveTimeout=2000;client.SendTimeout=2000;using(var stream=client.GetStream()){var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);string line=reader.ReadLine();int size=0;string h;while(!string.IsNullOrEmpty(h=reader.ReadLine())){size+=h.Length;if(size>8192)throw new IOException();}string body,kind,code;if(line=="GET /api/snapshot HTTP/1.1"){body=Snapshot();kind="application/json";code="200 OK";}else if(line=="GET / HTTP/1.1"){body=File.ReadAllText("live.html",Encoding.UTF8);kind="text/html";code="200 OK";}else{body="Not found";kind="text/plain";code="404 Not Found";}var bytes=Encoding.UTF8.GetBytes(body);var header=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+"\r\nContent-Type: "+kind+"; charset=utf-8\r\nContent-Length: "+bytes.Length+"\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(bytes,0,bytes.Length);}}}catch(Exception){if(closing)break;}}});t.IsBackground=true;t.Start();}
}
