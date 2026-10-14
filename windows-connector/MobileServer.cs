using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// Only this authenticated listener is forwarded by the Internet tunnel.
class MobileServer : IDisposable {
 readonly Func<string> snapshot; readonly Func<string,string> history; readonly string root;
 readonly string token; readonly DateTime expires;
 TcpListener listener; volatile bool stopped; int clients;
 public int Port { get; private set; }
 public string PublicHost { get; set; }
 public DateTime Expires { get { return expires; } }
 public MobileServer(Func<string> snapshotReader,string dashboardRoot,Func<string,string> historyReader=null){snapshot=snapshotReader;history=historyReader;root=Path.GetFullPath(dashboardRoot);var bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);token=BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();expires=DateTime.UtcNow.AddHours(8);}
 public string PairUrl(string baseUrl){return baseUrl+"/pair?token="+token;}
 public void Start(){for(int p=8885;p<=8905;p++){var candidate=new TcpListener(IPAddress.Loopback,p);try{candidate.Start();listener=candidate;Port=p;break;}catch(SocketException ex){candidate.Stop();if(ex.SocketErrorCode!=SocketError.AddressAlreadyInUse)throw;}}if(listener==null)throw new SocketException((int)SocketError.AddressAlreadyInUse);var thread=new Thread(AcceptLoop);thread.IsBackground=true;thread.Start();}
 void AcceptLoop(){while(!stopped){try{var client=listener.AcceptTcpClient();if(Interlocked.Increment(ref clients)>8){Interlocked.Decrement(ref clients);client.Close();continue;}ThreadPool.QueueUserWorkItem(_=>{try{Handle(client);}catch(Exception){}finally{client.Close();Interlocked.Decrement(ref clients);}});}catch(SocketException){if(stopped)return;}catch(ObjectDisposedException){return;}}}
 bool Valid(string candidate){if(candidate==null||candidate.Length!=token.Length)return false;int diff=0;for(int i=0;i<token.Length;i++)diff|=candidate[i]^token[i];return diff==0&&DateTime.UtcNow<expires;}
 void Handle(TcpClient client){client.ReceiveTimeout=3000;client.SendTimeout=3000;var stream=client.GetStream();string request=ReadLine(stream,2048);if(request==null)return;var parts=request.Split(' ');if(parts.Length!=3||parts[0]!="GET"){Reply(stream,"405 Method Not Allowed","text/plain","GET only",null);return;}var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);int total=request.Length;while(true){string h=ReadLine(stream,8192-total);if(h==null)throw new IOException();if(h.Length==0)break;total+=h.Length;int colon=h.IndexOf(':');if(colon<1||headers.ContainsKey(h.Substring(0,colon)))throw new IOException();headers[h.Substring(0,colon)]=h.Substring(colon+1).Trim();}
  string host;if(!headers.TryGetValue("Host",out host)||(host!="127.0.0.1:"+Port&&host!="localhost:"+Port&&!string.Equals(host,PublicHost,StringComparison.OrdinalIgnoreCase))){Reply(stream,"400 Bad Request","text/plain","Invalid host",null);return;}
  string target=parts[1];if(target.StartsWith("/pair?token=",StringComparison.Ordinal)){string supplied=target.Substring(12);if(!Valid(supplied)){Reply(stream,"401 Unauthorized","text/plain","Pairing link is invalid or expired. Open the connector on your computer.",null);return;}Reply(stream,"302 Found","text/plain","Paired",new Dictionary<string,string>{{"Location","/?mode=live"},{"Set-Cookie","mega_phone="+token+"; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age="+(int)(expires-DateTime.UtcNow).TotalSeconds}});return;}
  string cookie,session=null;if(headers.TryGetValue("Cookie",out cookie))foreach(string item in cookie.Split(';')){var pair=item.Trim().Split(new[]{'='},2);if(pair.Length==2&&pair[0]=="mega_phone")session=pair[1];}
  if(!Valid(session)){Reply(stream,"401 Unauthorized","text/plain","Open the pairing link from your computer first. Do not share it.",null);return;}
  string path=target.Split('?')[0];if(path=="/api/snapshot"){Reply(stream,"200 OK","application/json",snapshot(),null);return;}
  if(path=="/api/history"&&history!=null){Reply(stream,"200 OK","application/json",history(target),null);return;}
  if(path=="/"){string html=File.ReadAllText(Path.Combine(root,"index.html"),Encoding.UTF8).Replace("window.liveMode=new URLSearchParams(location.search).get(\"mode\")===\"live\";","window.liveMode=true;window.remotePhoneMode=true;");Reply(stream,"200 OK","text/html",html,null);return;}
  if(path=="/large-order-alerts.js"||path=="/dashboard-live.js"||path=="/live-flow.js"||path=="/market-history.js"){Reply(stream,"200 OK","text/javascript",File.ReadAllText(Path.Combine(root,path.Substring(1)),Encoding.UTF8),null);return;}
  Reply(stream,"404 Not Found","text/plain","Not found",null);
 }
 static string ReadLine(NetworkStream stream,int limit){if(limit<=0)throw new IOException();var bytes=new List<byte>();int c;while((c=stream.ReadByte())!=-1){if(c==10){if(bytes.Count>0&&bytes[bytes.Count-1]==13)bytes.RemoveAt(bytes.Count-1);return Encoding.ASCII.GetString(bytes.ToArray());}if(bytes.Count>=limit)throw new IOException();bytes.Add((byte)c);}return null;}
 static void Reply(NetworkStream stream,string status,string kind,string body,Dictionary<string,string> extra){var data=Encoding.UTF8.GetBytes(body);var header=new StringBuilder("HTTP/1.1 "+status+"\r\nContent-Type: "+kind+"; charset=utf-8\r\nContent-Length: "+data.Length+"\r\nCache-Control: no-store\r\nConnection: close\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nContent-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'\r\n");if(extra!=null)foreach(var pair in extra)header.Append(pair.Key).Append(": ").Append(pair.Value).Append("\r\n");header.Append("\r\n");var raw=Encoding.ASCII.GetBytes(header.ToString());stream.Write(raw,0,raw.Length);stream.Write(data,0,data.Length);}
 public void Dispose(){stopped=true;if(listener!=null)listener.Stop();}
}
