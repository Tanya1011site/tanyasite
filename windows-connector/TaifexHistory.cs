using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;

static class MarketCsv {
 public static List<string> Row(string line){var list=new List<string>();var cell=new StringBuilder();bool quoted=false;for(int i=0;i<line.Length;i++){char c=line[i];if(c=='"'){if(quoted&&i+1<line.Length&&line[i+1]=='"'){cell.Append('"');i++;}else quoted=!quoted;}else if(c==','&&!quoted){list.Add(cell.ToString().Trim());cell.Clear();}else cell.Append(c);}if(quoted)throw new InvalidDataException("CSV 引號未關閉");list.Add(cell.ToString().Trim());return list;}
 public static Dictionary<string,int> Headers(string line){var list=Row(line);var map=new Dictionary<string,int>();for(int i=0;i<list.Count;i++)map[list[i].Trim('\uFEFF').Replace(" ","").Replace("（","(").Replace("）",")")]=i;return map;}
 public static int Index(Dictionary<string,int> h,params string[] names){foreach(var name in names){int i;if(h.TryGetValue(name,out i))return i;}return -1;}
 public static string Cell(List<string> row,int index){return index>=0&&index<row.Count?row[index]:"";}
 public static DateTime Date(string value){DateTime d;if(!DateTime.TryParseExact(value,new[]{"yyyy/MM/dd","yyyy-MM-dd","yyyyMMdd"},CultureInfo.InvariantCulture,DateTimeStyles.None,out d)||d.Year<2000||d.Year>2100)throw new InvalidDataException("日期格式無法辨識");return d;}
 public static double? Number(string value){double n;return double.TryParse(value.Replace(",",""),NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&!double.IsNaN(n)&&!double.IsInfinity(n)?(double?)n:null;}
 public static string Future(string code){return code=="TX"||code=="TXF"?"TXF":code=="MTX"||code=="MXF"?"MXF":code=="TMF"?"TMF":null;}
 public static string OptionSide(string value){return value=="買權"||value=="C"||value=="CALL"?"call":value=="賣權"||value=="P"||value=="PUT"?"put":null;}
 public static string OptionSymbol(string month,double strike,string side){int year=int.Parse(month.Substring(0,4)),m=int.Parse(month.Substring(4,2));return "TXO"+strike.ToString("0.########",CultureInfo.InvariantCulture)+(char)((side=="call"?'A':'M')+m-1)+(year%10);}
 public static string Decode(byte[] bytes){try{return new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){return Encoding.GetEncoding(950).GetString(bytes);}}
}
[DataContract]class DailyRecord {
 [DataMember]public string date,symbol,kind,month,session,source;
 [DataMember]public double strike,open,high,low,close;
 [DataMember]public long? volume,openInterest;
}
[DataContract]class OpenInterestWeek {
 [DataMember]public string symbol,kind,date,source;
 [DataMember]public double strike;
 [DataMember]public long? openInterest,dailyChange,weekChange;
 [DataMember]public long reportedVolume;
 [DataMember]public int reportDays;
}
class TaifexDailyStore {
 readonly object gate=new object();readonly string file;readonly Dictionary<string,DailyRecord> rows=new Dictionary<string,DailyRecord>();
 public string Status {get;private set;}
 public TaifexDailyStore(string root){Directory.CreateDirectory(root);file=Path.Combine(root,"taifex-daily.json");Status="尚未取得期交所日報，未平倉不可由成交量反推";if(File.Exists(file)){try{foreach(var row in MarketJson.Read<List<DailyRecord>>(File.ReadAllText(file,Encoding.UTF8)))rows[Key(row)]=row;Status="已載入保存的日報；請查看公布日期";}catch(Exception){Status="日報存檔無法解析；原檔已保留";}}}
 static string Key(DailyRecord r){return r.date+"|"+r.symbol+"|"+r.session;}
 static long? Integer(string value){var n=MarketCsv.Number(value);return n.HasValue&&n.Value>=0&&n.Value==Math.Truncate(n.Value)&&n.Value<=long.MaxValue?(long?)n.Value:null;}
 public int Import(string text,string source){using(var reader=new StringReader(text)){string header=reader.ReadLine();if(header==null)throw new InvalidDataException("日報 CSV 為空");var h=MarketCsv.Headers(header);int date=MarketCsv.Index(h,"交易日期"),contract=MarketCsv.Index(h,"契約","商品代號"),month=MarketCsv.Index(h,"到期月份(週別)"),oi=MarketCsv.Index(h,"未沖銷契約數","未平倉量"),volume=MarketCsv.Index(h,"成交量"),strike=MarketCsv.Index(h,"履約價","履約價格"),cp=MarketCsv.Index(h,"買賣權","買賣權別"),session=MarketCsv.Index(h,"交易時段"),open=MarketCsv.Index(h,"開盤價"),high=MarketCsv.Index(h,"最高價"),low=MarketCsv.Index(h,"最低價"),close=MarketCsv.Index(h,"收盤價");if(date<0||contract<0||month<0||oi<0)throw new InvalidDataException("找不到期交所日報的日期、商品、月份或未平倉欄位；未匯入");var imported=new List<DailyRecord>();string line;while((line=reader.ReadLine())!=null){if(line.Length==0)continue;var cells=MarketCsv.Row(line);string maturity=MarketCsv.Cell(cells,month);if(maturity.Length!=6||!maturity.All(char.IsDigit)||int.Parse(maturity.Substring(4,2))<1||int.Parse(maturity.Substring(4,2))>12)continue;string code=MarketCsv.Cell(cells,contract).ToUpperInvariant(),prefix=MarketCsv.Future(code),side=MarketCsv.OptionSide(MarketCsv.Cell(cells,cp).ToUpperInvariant());double k=MarketCsv.Number(MarketCsv.Cell(cells,strike))??0;if(prefix==null&&(code!="TXO"||side==null||k<=0))continue;string day=MarketCsv.Date(MarketCsv.Cell(cells,date)).ToString("yyyy-MM-dd");string period=MarketCsv.Cell(cells,session);var r=new DailyRecord(){date=day,month=maturity,kind=prefix!=null?"future":side,strike=k,symbol=prefix!=null?MonthlyContract.SymbolFor(prefix,maturity):MarketCsv.OptionSymbol(maturity,k,side),session=period.Contains("盤後")||period.Contains("夜")?"night":"day",source=source,volume=Integer(MarketCsv.Cell(cells,volume)),openInterest=Integer(MarketCsv.Cell(cells,oi)),open=MarketCsv.Number(MarketCsv.Cell(cells,open))??0,high=MarketCsv.Number(MarketCsv.Cell(cells,high))??0,low=MarketCsv.Number(MarketCsv.Cell(cells,low))??0,close=MarketCsv.Number(MarketCsv.Cell(cells,close))??0};imported.Add(r);}if(imported.Count==0)throw new InvalidDataException("日報沒有可辨識的台指單月資料");lock(gate){foreach(var row in imported)rows[Key(row)]=row;Save();Status="已匯入 "+imported.Count+" 筆日報；未平倉為盤後公布值";}return imported.Count;}}
 void Save(){string temp=file+".tmp";File.WriteAllText(temp,MarketJson.Write(rows.Values.ToList()),new UTF8Encoding(false));if(File.Exists(file)){string backup=file+".bak";if(File.Exists(backup))File.Delete(backup);File.Replace(temp,file,backup);}else File.Move(temp,file);}
 public List<OpenInterestWeek> Week(string month,DateTime taipei){string from=taipei.Date.AddDays(-6).ToString("yyyy-MM-dd");lock(gate){return rows.Values.Where(r=>r.month==month&&string.CompareOrdinal(r.date,taipei.ToString("yyyy-MM-dd"))<=0).GroupBy(r=>r.symbol).Select(g=>{var days=g.Where(r=>r.session=="day"&&r.openInterest.HasValue).OrderBy(r=>r.date).ToList();var latest=days.LastOrDefault();if(latest==null)return null;var prior=days.Where(r=>string.CompareOrdinal(r.date,latest.date)<0).LastOrDefault();var baseline=days.Where(r=>string.CompareOrdinal(r.date,from)<0).LastOrDefault();return new OpenInterestWeek(){symbol=g.Key,kind=latest.kind,strike=latest.strike,date=latest.date,source=latest.source,openInterest=latest.openInterest,dailyChange=prior==null?null:latest.openInterest-prior.openInterest,weekChange=baseline==null||string.CompareOrdinal(latest.date,from)<0?null:latest.openInterest-baseline.openInterest,reportedVolume=g.Where(r=>string.CompareOrdinal(r.date,from)>=0).Sum(r=>r.volume??0),reportDays=g.Where(r=>string.CompareOrdinal(r.date,from)>=0).Select(r=>r.date).Distinct().Count()};}).Where(r=>r!=null).OrderByDescending(r=>r.strike).ThenBy(r=>r.kind).ToList();}}
 public List<DailyRecord> Records(string month){lock(gate)return rows.Values.Where(r=>r.month==month).OrderBy(r=>r.date).ToList();}
 public List<ArchiveSession> Ranges(string symbol){lock(gate)return rows.Values.Where(r=>r.symbol==symbol&&r.high>0&&r.low>0&&r.high>=r.low).Select(r=>new ArchiveSession(){symbol=symbol,date=r.date,session=r.session,source=r.source+" · 交易所交易日（沒有逐筆轉折）",open=r.open,high=r.high,low=r.low,close=r.close,volume=r.volume??0,firstReceipt=MarketSessions.Utc(MarketCsv.Date(r.date)),lastReceipt=MarketSessions.Utc(MarketCsv.Date(r.date).AddHours(23)),threshold=Math.Max(10,(r.high-r.low)*.1)}).OrderBy(r=>r.lastReceipt).ToList();}
 public int Download(DateTime taipei){int total=0;var errors=new List<string>();string start=taipei.Date.AddDays(-10).ToString("yyyy/MM/dd"),end=taipei.Date.ToString("yyyy/MM/dd");foreach(string code in new[]{"TX","MTX","TMF","TXO"})try{Status="正在下載日報："+code;string url="https://www.taifex.com.tw/cht/3/"+(code=="TXO"?"optDataDown":"futDataDown")+"?down_type=1&commodity_id="+code+"&queryStartDate="+Uri.EscapeDataString(start)+"&queryEndDate="+Uri.EscapeDataString(end);ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;var request=(HttpWebRequest)WebRequest.Create(url);request.Timeout=30000;request.ReadWriteTimeout=30000;request.UserAgent="TanyaSiteMarketHistory/1.0";using(var response=request.GetResponse())using(var stream=response.GetResponseStream())using(var buffer=new MemoryStream()){var bytes=new byte[8192];int n;while((n=stream.Read(bytes,0,bytes.Length))>0){buffer.Write(bytes,0,n);if(buffer.Length>50*1024*1024)throw new InvalidDataException("日報超過安全大小限制");}total+=Import(MarketCsv.Decode(buffer.ToArray()),"期交所 HTTPS 日報");}}catch(Exception ex){errors.Add(code+":"+ex.GetType().Name);}lock(gate)Status=errors.Count>0?"日報回補未完整："+string.Join("、",errors)+"；可手動匯入期交所 CSV":"已下載期交所日報；請核對最新公布日期";return total;}
}
