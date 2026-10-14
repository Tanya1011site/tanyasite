using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

[DataContract] class ArchiveMinute {
 [DataMember] public string symbol,kind,session,sessionDate,source="本機接收";
 [DataMember] public long time,volume,buyQty,sellQty,unknownQty,largeBuyQty,largeSellQty,largeUnknownQty,quoteReceipt;
 [DataMember] public double open,high,low,close,bid,ask,strike;
 [DataMember] public long firstTrade,lastTrade;
 [DataMember] public int count,largeCount;
 public long version;
 public ArchiveMinute Copy(){return (ArchiveMinute)MemberwiseClone();}
}
[DataContract] class WavePoint { [DataMember]public long time; [DataMember]public double price; [DataMember]public string kind; }
[DataContract] class ArchiveSession {
 [DataMember]public string symbol,date,session,source="本機逐筆接收（未回補完整盤段）";
 [DataMember]public long firstReceipt,lastReceipt,volume;
 [DataMember]public double open,high,low,close,threshold;
 [DataMember]public List<WavePoint> pivots=new List<WavePoint>();
 [DataMember]public int direction; [DataMember]public bool unordered;
 [DataMember]public double candidate,candidateLow,candidateHigh;
 [DataMember]public long candidateTime,lowTime,highTime;
 public long version;
 public ArchiveSession Copy(){var c=(ArchiveSession)MemberwiseClone();c.pivots=(pivots??new List<WavePoint>()).Select(p=>new WavePoint(){time=p.time,price=p.price,kind=p.kind}).ToList();return c;}
}
[DataContract] class ArchiveBigTrade {
 [DataMember]public string id,feed,symbol,kind,side;
 [DataMember]public long seq,receipt;
 [DataMember]public double price,multiplier;
 [DataMember]public int qty;
 [DataMember]public decimal? premiumAmount;
}
[DataContract] class ArchiveEntry {
 [DataMember(EmitDefaultValue=false)]public ArchiveMinute minute;
 [DataMember(EmitDefaultValue=false)]public ArchiveSession session;
 [DataMember(EmitDefaultValue=false)]public ArchiveBigTrade big;
 [DataMember(EmitDefaultValue=false)]public string deletedBig;
}
[DataContract] class ArchiveAggregate {
 [DataMember]public string symbol,kind;
 [DataMember]public double strike;
 [DataMember]public long volume,buyQty,sellQty,unknownQty,largeBuyQty,largeSellQty,largeUnknownQty;
 [DataMember]public int count,largeCount;
}
[DataContract] class ArchiveSummary {
 [DataMember]public string status,period="近 7 個日曆日（臺北），僅已接收部分";
 [DataMember]public List<string> dates,sessionDates;
 [DataMember]public long fromUtc,lastUtc;
 [DataMember]public List<ArchiveAggregate> weekly;
 [DataMember]public List<ArchiveSession> sessions;
 [DataMember]public int damagedLines;
}
[DataContract]class ArchiveHistory {
 [DataMember]public string symbol,date,session,status="本機接收時間彙整；空白時段未回補";
 [DataMember]public List<ArchiveMinute> minutes;
}
static class MarketJson {
 public static string Write<T>(T value){using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(T)).WriteObject(stream,value);return Encoding.UTF8.GetString(stream.ToArray());}}
 public static T Read<T>(string value){using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(value)))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}
}
static class MarketSessions {
 public static DateTime Taipei(long utc){return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(utc).AddHours(8);}
 public static long Utc(DateTime taipei){return (long)(DateTime.SpecifyKind(taipei,DateTimeKind.Unspecified).AddHours(-8)-new DateTime(1970,1,1)).TotalMilliseconds;}
 public static string Kind(long utc){var t=Taipei(utc).TimeOfDay;if(t>=new TimeSpan(8,45,0)&&t<=new TimeSpan(13,45,0))return "day";if(t>=new TimeSpan(15,0,0)||t<=new TimeSpan(5,0,0))return "night";return "off";}
 public static string Date(long utc){var d=Taipei(utc);if(Kind(utc)=="night"&&d.TimeOfDay<=new TimeSpan(5,0,0))d=d.AddDays(-1);return d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
}
class MarketArchive {
 readonly object gate=new object(),flushGate=new object();readonly string root;
 readonly Dictionary<string,ArchiveMinute> minutes=new Dictionary<string,ArchiveMinute>();
 readonly Dictionary<string,ArchiveSession> sessions=new Dictionary<string,ArchiveSession>();
 readonly Dictionary<string,ArchiveBigTrade> big=new Dictionary<string,ArchiveBigTrade>();
 readonly HashSet<string> dirtyMinutes=new HashSet<string>(),dirtySessions=new HashSet<string>(),dirtyBig=new HashSet<string>();
 readonly HashSet<string> deletedBig=new HashSet<string>();
 int damaged;string error="";
 public MarketArchive(string directory){root=directory;if(root!=null){Directory.CreateDirectory(root);Load();}}
 static string MinuteKey(string symbol,long time){return symbol+"|"+time;}
 static string SessionKey(string symbol,long time){return symbol+"|"+MarketSessions.Date(time)+"|"+MarketSessions.Kind(time);}
 void Load(){var from=DateTime.UtcNow.AddHours(8).Date.AddDays(-10).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);foreach(string file in Directory.GetFiles(root,"capture-*.jsonl").OrderBy(f=>f)){string day=Path.GetFileNameWithoutExtension(file).Substring(8);if(string.CompareOrdinal(day,from)<0)continue;try{foreach(string line in File.ReadLines(file,Encoding.UTF8)){try{if(line.Length>65536)throw new InvalidDataException();var e=MarketJson.Read<ArchiveEntry>(line);if(e.deletedBig!=null)big.Remove(e.deletedBig);if(e.minute!=null)minutes[MinuteKey(e.minute.symbol,e.minute.time)]=e.minute;if(e.session!=null)sessions[e.session.symbol+"|"+e.session.date+"|"+e.session.session]=e.session;if(e.big!=null)big[e.big.id]=e.big;}catch(Exception){damaged++;}}}catch(IOException){error="部分歷史檔案無法讀取；已保留原檔";}}}
 ArchiveMinute Minute(string symbol,string kind,double strike,long receipt){long time=receipt/60000*60000;string key=MinuteKey(symbol,time);ArchiveMinute m;if(!minutes.TryGetValue(key,out m)){m=new ArchiveMinute(){symbol=symbol,kind=kind,strike=strike,time=time,session=MarketSessions.Kind(receipt),sessionDate=MarketSessions.Date(receipt)};minutes.Add(key,m);}m.version++;dirtyMinutes.Add(key);return m;}
 public void Quote(string symbol,string kind,double strike,double bid,double ask,long receipt){if(double.IsNaN(bid)||double.IsNaN(ask)||double.IsInfinity(bid)||double.IsInfinity(ask))return;lock(gate){var m=Minute(symbol,kind,strike,receipt);m.bid=bid;m.ask=ask;m.quoteReceipt=receipt;}}
 public void Trade(string symbol,string kind,double strike,double price,int qty,string side,long receipt,double multiplier,string feed,long seq){lock(gate){var m=Minute(symbol,kind,strike,receipt);if(m.count==0){m.open=m.high=m.low=price;m.firstTrade=receipt;}if(receipt<m.firstTrade){m.firstTrade=receipt;m.open=price;}m.high=Math.Max(m.high,price);m.low=Math.Min(m.low,price);if(receipt>=m.lastTrade){m.lastTrade=receipt;m.close=price;}m.volume+=qty;m.count++;if(side=="buy")m.buyQty+=qty;else if(side=="sell")m.sellQty+=qty;else m.unknownQty+=qty;int limit=kind=="future"?300:50;if(qty>=limit){m.largeCount++;if(side=="buy")m.largeBuyQty+=qty;else if(side=="sell")m.largeSellQty+=qty;else m.largeUnknownQty+=qty;var row=new ArchiveBigTrade(){id=feed+":"+seq,feed=feed,seq=seq,receipt=receipt,price=price,qty=qty,side=side,symbol=symbol,kind=kind,multiplier=multiplier};if(kind!="future"&&multiplier>0)row.premiumAmount=(decimal)price*(decimal)multiplier*qty;big[row.id]=row;dirtyBig.Add(row.id);}if(kind=="future"&&MarketSessions.Kind(receipt)!="off")Session(symbol,price,qty,receipt);}}
 void Session(string symbol,double price,int qty,long receipt){string key=SessionKey(symbol,receipt);ArchiveSession s;if(!sessions.TryGetValue(key,out s)){s=new ArchiveSession(){symbol=symbol,date=MarketSessions.Date(receipt),session=MarketSessions.Kind(receipt),firstReceipt=receipt,open=price,high=price,low=price,candidate=price,candidateLow=price,candidateHigh=price,candidateTime=receipt,lowTime=receipt,highTime=receipt};sessions[key]=s;}if(receipt<s.lastReceipt){s.unordered=true;s.pivots.Clear();}s.version++;dirtySessions.Add(key);s.high=Math.Max(s.high,price);s.low=Math.Min(s.low,price);if(receipt>=s.lastReceipt)s.close=price;if(receipt<s.firstReceipt){s.firstReceipt=receipt;s.open=price;}s.volume+=qty;s.lastReceipt=Math.Max(receipt,s.lastReceipt);s.threshold=Math.Max(10,(s.high-s.low)*.1);if(s.unordered)return;if(s.direction==0){if(price<s.candidateLow){s.candidateLow=price;s.lowTime=receipt;}if(price>s.candidateHigh){s.candidateHigh=price;s.highTime=receipt;}if(price-s.candidateLow>=s.threshold){s.pivots.Add(new WavePoint(){time=s.lowTime,price=s.candidateLow,kind="low"});s.direction=1;s.candidate=price;s.candidateTime=receipt;}else if(s.candidateHigh-price>=s.threshold){s.pivots.Add(new WavePoint(){time=s.highTime,price=s.candidateHigh,kind="high"});s.direction=-1;s.candidate=price;s.candidateTime=receipt;}}else if(s.direction>0){if(price>s.candidate){s.candidate=price;s.candidateTime=receipt;}if(s.candidate-price>=s.threshold){s.pivots.Add(new WavePoint(){time=s.candidateTime,price=s.candidate,kind="high"});s.direction=-1;s.candidate=price;s.candidateTime=receipt;}}else{if(price<s.candidate){s.candidate=price;s.candidateTime=receipt;}if(price-s.candidate>=s.threshold){s.pivots.Add(new WavePoint(){time=s.candidateTime,price=s.candidate,kind="low"});s.direction=1;s.candidate=price;s.candidateTime=receipt;}}if(s.pivots.Count>40)s.pivots.RemoveAt(0);}
 public void Flush(){if(root==null)return;lock(flushGate){List<ArchiveMinute> ms;List<ArchiveSession> ss;List<ArchiveBigTrade> bs;List<string> removed;lock(gate){removed=deletedBig.ToList();ms=dirtyMinutes.Select(k=>minutes[k].Copy()).ToList();ss=dirtySessions.Select(k=>sessions[k].Copy()).ToList();bs=dirtyBig.Select(k=>big[k]).ToList();}var writes=new Dictionary<string,List<string>>();Action<long,string> add=(time,json)=>{string file=Path.Combine(root,"capture-"+MarketSessions.Taipei(time).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)+".jsonl");if(!writes.ContainsKey(file))writes[file]=new List<string>();writes[file].Add(json);};foreach(var m in ms)add(m.time,MarketJson.Write(new ArchiveEntry(){minute=m}));foreach(var s in ss)add(s.lastReceipt,MarketJson.Write(new ArchiveEntry(){session=s}));foreach(string id in removed)add(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),MarketJson.Write(new ArchiveEntry(){deletedBig=id}));foreach(var b in bs)add(b.receipt,MarketJson.Write(new ArchiveEntry(){big=b}));try{foreach(var pair in writes)File.AppendAllLines(pair.Key,pair.Value,new UTF8Encoding(false));lock(gate){foreach(var m in ms){string key=MinuteKey(m.symbol,m.time);if(minutes[key].version==m.version)dirtyMinutes.Remove(key);}foreach(var s in ss){string key=s.symbol+"|"+s.date+"|"+s.session;if(sessions[key].version==s.version)dirtySessions.Remove(key);}foreach(var b in bs)dirtyBig.Remove(b.id);foreach(string id in removed)deletedBig.Remove(id);error="";}}catch(IOException){lock(gate)error="歷史保存失敗，請檢查磁碟與權限；待重試";}catch(UnauthorizedAccessException){lock(gate)error="歷史目錄無寫入權限；待重試";}}}
 static long Cutoff(long now){return MarketSessions.Utc(MarketSessions.Taipei(now).Date.AddDays(-6));}
 public ArchiveSummary Summary(long now,string primary){lock(gate){long from=Cutoff(now);var values=minutes.Values.Where(m=>m.time>=from).ToList();var weekly=values.Where(m=>m.count>0).GroupBy(m=>m.symbol).Select(g=>new ArchiveAggregate(){symbol=g.Key,kind=g.First().kind,strike=g.First().strike,volume=g.Sum(m=>m.volume),buyQty=g.Sum(m=>m.buyQty),sellQty=g.Sum(m=>m.sellQty),unknownQty=g.Sum(m=>m.unknownQty),largeBuyQty=g.Sum(m=>m.largeBuyQty),largeSellQty=g.Sum(m=>m.largeSellQty),largeUnknownQty=g.Sum(m=>m.largeUnknownQty),count=g.Sum(m=>m.count),largeCount=g.Sum(m=>m.largeCount)}).OrderBy(m=>m.symbol).ToList();return new ArchiveSummary(){status=error.Length>0?error:"本機歷史已啟用；未接收時段沒有回補",fromUtc=from,lastUtc=values.Count>0?values.Max(m=>m.time):0,dates=values.Select(m=>MarketSessions.Taipei(m.time).ToString("yyyy-MM-dd")).Distinct().OrderBy(d=>d).ToList(),sessionDates=values.Select(m=>m.sessionDate).Distinct().OrderBy(d=>d).ToList(),weekly=weekly,sessions=sessions.Values.Where(s=>s.symbol==primary&&s.lastReceipt>=from).OrderBy(s=>s.lastReceipt).Select(s=>s.Copy()).ToList(),damagedLines=damaged};}}
 public void ImportHistorical(MarketArchive other,string source){List<ArchiveMinute> ms;List<ArchiveSession> ss;List<ArchiveBigTrade> bs;lock(other.gate){ms=other.minutes.Values.Select(m=>m.Copy()).ToList();ss=other.sessions.Values.Select(s=>s.Copy()).ToList();bs=other.big.Values.ToList();}lock(gate){var replaced=new HashSet<string>(ms.Select(m=>MinuteKey(m.symbol,m.time)));foreach(var old in big.Values.Where(b=>replaced.Contains(MinuteKey(b.symbol,b.receipt/60000*60000))).ToList()){big.Remove(old.id);dirtyBig.Remove(old.id);deletedBig.Add(old.id);}foreach(var m in ms){m.source=source;string key=MinuteKey(m.symbol,m.time);ArchiveMinute previous;if(minutes.TryGetValue(key,out previous))m.version=previous.version+1;else m.version=1;minutes[key]=m;dirtyMinutes.Add(key);}foreach(var s in ss){s.source=source;string key=s.symbol+"|"+s.date+"|"+s.session;ArchiveSession previous;if(sessions.TryGetValue(key,out previous))s.version=previous.version+1;else s.version=1;sessions[key]=s;dirtySessions.Add(key);}foreach(var b in bs){big[b.id]=b;dirtyBig.Add(b.id);deletedBig.Remove(b.id);}}Flush();}
 public List<ArchiveMinute> Recent(string symbol,int limit){lock(gate)return minutes.Values.Where(m=>m.symbol==symbol&&m.count>0).OrderByDescending(m=>m.time).Take(limit).OrderBy(m=>m.time).Select(m=>m.Copy()).ToList();}
 public List<ArchiveMinute> History(string symbol,string date,string session){lock(gate)return minutes.Values.Where(m=>m.symbol==symbol&&m.sessionDate==date&&(session=="all"||m.session==session)).OrderBy(m=>m.time).Select(m=>m.Copy()).ToList();}
 public List<ArchiveBigTrade> BigHistory(string code,string date,string period,int limit){lock(gate)return big.Values.Where(b=>b.symbol==code&&MarketSessions.Date(b.receipt)==date&&(period=="all"||MarketSessions.Kind(b.receipt)==period)).OrderByDescending(b=>b.receipt).Take(limit).OrderBy(b=>b.receipt).ToList();}
 public List<ArchiveBigTrade> Big(string feed,int limit){lock(gate)return big.Values.Where(b=>feed==null||b.feed==feed).OrderByDescending(b=>b.receipt).Take(limit).OrderBy(b=>b.receipt).ToList();}
}
