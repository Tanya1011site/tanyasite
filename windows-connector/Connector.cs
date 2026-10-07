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
 ComboBox contracts=new ComboBox(); Label status=new Label(), last=new Label();
 spdQuoteAPI api; readonly object gate=new object(); readonly List<Candle> bars=new List<Candle>();
 string symbol="", state="尚未登入", matchTime=""; bool authenticated=false, subscribed=false; long lastReceipt=0;
 TcpListener server; bool closing=false;
 const int LocalPort=8765;
 [STAThread] static void Main() { Application.EnableVisualStyles(); Application.Run(new Connector()); }
 public Connector() {
  Text="兆豐行情連接 · 僅接收行情"; Width=620; Height=400; Font=new Font("Microsoft JhengHei",10);
  var labels=new string[]{"行情主機", "連接埠", "行情帳號", "行情密碼"}; var fields=new TextBox[]{host,port,user,password};
  for(int i=0;i<4;i++){Controls.Add(new Label(){Text=labels[i],Left=20,Top=24+i*40,Width=110});fields[i].SetBounds(140,20+i*40,420,28);Controls.Add(fields[i]);}
  password.UseSystemPasswordChar=true;
  login.Text="1. 登入行情";login.SetBounds(20,190,150,35);login.Click+=(s,e)=>Login();Controls.Add(login);
  contracts.SetBounds(20,235,380,30);contracts.DropDownStyle=ComboBoxStyle.DropDownList;Controls.Add(contracts);
  subscribe.Text="2. 訂閱商品";subscribe.SetBounds(410,235,150,30);subscribe.Enabled=false;subscribe.Click+=(s,e)=>Subscribe();Controls.Add(subscribe);
  status.SetBounds(20,280,560,26);status.Text=state;Controls.Add(status);
  last.SetBounds(20,308,560,25);last.Text="登入資料只保留於本次程式記憶體，不儲存檔案。";Controls.Add(last);
  view.Text="3. 開啟看盤";view.SetBounds(410,190,150,35);view.Enabled=false;view.Click+=(s,e)=>System.Diagnostics.Process.Start("http://127.0.0.1:"+LocalPort+"/");Controls.Add(view);
  FormClosing+=(s,e)=>{closing=true;if(server!=null)server.Stop();};
 }
 void UI(Action a){if(!closing&&!IsDisposed)try{BeginInvoke(a);}catch(InvalidOperationException){}}
 void SetState(string text){lock(gate)state=text;UI(()=>status.Text=text);}
 void Login(){int p;if(string.IsNullOrWhiteSpace(host.Text)||!int.TryParse(port.Text,out p)||p<1||p>65535||string.IsNullOrWhiteSpace(user.Text)||password.Text.Length==0){MessageBox.Show("請填入兆豐提供的行情主機、連接埠、帳號與密碼。");return;}
  login.Enabled=false;
  try{api=new spdQuoteAPI();
   api.evOnConnected=new OnQuoteConnected(()=>SetState("主機已連接，等待登入確認"));
   api.evOnDisconnected=new OnQuoteDisconnected(()=>{authenticated=false;SetState("已斷線：畫面保留最後資料，請關閉程式後重新登入");UI(()=>subscribe.Enabled=false);});
   api.evOnLogonResponse=new OnQuoteLogonResponse((ok,msg)=>{authenticated=ok;SetState(ok?"登入成功，等待商品清單":"登入失敗，請確認權限及資料後重新啟動");});
   api.evOnContractDownloadComplete=new OnQuoteContractDownloadComplete(n=>UI(()=>{contracts.Items.Clear();foreach(var key in api.Futures.Keys)contracts.Items.Add("期貨 | "+key);foreach(var key in api.Options.Keys)contracts.Items.Add("選擇權 | "+key);if(contracts.Items.Count>0)contracts.SelectedIndex=0;subscribe.Enabled=authenticated&&contracts.Items.Count>0;SetState("商品清單已下載，請選擇目前交易的合約");}));
   api.evOnTrade=new OnQuoteTrade(Trade);
   StartServer();
   api.Logon(host.Text.Trim(),p,user.Text.Trim(),password.Text,true); password.Clear();
  }catch(Exception ex){SetState("無法啟動："+ex.GetType().Name+"。請確認官方套件與設定檔齊全，關閉後重試。");}
 }
 void Subscribe(){if(!authenticated||contracts.SelectedItem==null||subscribed)return;var text=contracts.SelectedItem.ToString();var key=text.Substring(text.IndexOf(" | ")+3);lock(gate){symbol=key;bars.Clear();lastReceipt=0;}try{if(text.StartsWith("期貨"))api.SubscribeContract(api.Futures[key]);else api.SubscribeContract(api.Options[key]);subscribed=true;subscribe.Enabled=false;contracts.Enabled=false;view.Enabled=true;SetState("已送出订閱，等待成交。切換商品請重新啟動程式。");}catch(Exception ex){SetState("訂閱失敗："+ex.GetType().Name);}}
 void Trade(string exchange,string code,string time,double price,int qty,bool test){if(test||price<=0||double.IsNaN(price)||double.IsInfinity(price)||qty<=0)return;lock(gate){if(code!=symbol)return;var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();long minute=now/60000*60000;Candle c;if(bars.Count==0||bars[bars.Count-1].time!=minute){c=new Candle(){time=minute,open=price,high=price,low=price,close=price};bars.Add(c);if(bars.Count>300)bars.RemoveAt(0);}else c=bars[bars.Count-1];c.high=Math.Max(c.high,price);c.low=Math.Min(c.low,price);c.close=price;c.volume+=qty;lastReceipt=now;matchTime=time;state="已接收真實成交";}UI(()=>{last.Text="商品 "+code+" ｜成交 "+price+" ｜單筆 "+qty+" ｜行情時間 "+time;status.Text="已接收真實成交";});}
 static string Escape(string s){var b=new StringBuilder();foreach(char c in s){if(c=='"'||c=='\\')b.Append('\\').Append(c);else if(c<32)b.Append("\\u").Append(((int)c).ToString("x4"));else b.Append(c);}return b.ToString();}
 string Snapshot(){lock(gate){var b=new StringBuilder();b.Append("{\"status\":\"").Append(Escape(state)).Append("\",\"symbol\":\"").Append(Escape(symbol)).Append("\",\"connected\":").Append(authenticated?"true":"false").Append(",\"lastReceipt\":").Append(lastReceipt).Append(",\"matchTime\":\"").Append(Escape(matchTime)).Append("\",\"candles\":[");for(int i=0;i<bars.Count;i++){if(i>0)b.Append(',');var c=bars[i];b.Append("{\"time\":").Append(c.time).Append(",\"open\":").Append(c.open.ToString(CultureInfo.InvariantCulture)).Append(",\"high\":").Append(c.high.ToString(CultureInfo.InvariantCulture)).Append(",\"low\":").Append(c.low.ToString(CultureInfo.InvariantCulture)).Append(",\"close\":").Append(c.close.ToString(CultureInfo.InvariantCulture)).Append(",\"volume\":").Append(c.volume).Append('}');}return b.Append("]}").ToString();}}
 void StartServer(){server=new TcpListener(IPAddress.Loopback,LocalPort);server.Start();var t=new Thread(()=>{while(!closing){try{using(var client=server.AcceptTcpClient()){client.ReceiveTimeout=2000;client.SendTimeout=2000;using(var stream=client.GetStream()){var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);string line=reader.ReadLine();int size=0;string h;while(!string.IsNullOrEmpty(h=reader.ReadLine())){size+=h.Length;if(size>8192)throw new IOException();}string body,kind,code;if(line=="GET /api/snapshot HTTP/1.1"){body=Snapshot();kind="application/json";code="200 OK";}else if(line=="GET / HTTP/1.1"){body=File.ReadAllText("live.html",Encoding.UTF8);kind="text/html";code="200 OK";}else{body="Not found";kind="text/plain";code="404 Not Found";}var bytes=Encoding.UTF8.GetBytes(body);var header=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+"\r\nContent-Type: "+kind+"; charset=utf-8\r\nContent-Length: "+bytes.Length+"\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n");stream.Write(header,0,header.Length);stream.Write(bytes,0,bytes.Length);}}}catch(Exception){if(closing)break;}}});t.IsBackground=true;t.Start();}
}
