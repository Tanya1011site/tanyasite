using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

class OptionDescriptor {
 public string Symbol, Group, Prefix, Expiry, Side;
 public double Strike;
 public DateTime ExpiryDate;
 public static OptionDescriptor Parse(string symbol,string maturity,double strike,string callPut){
  var match=Regex.Match(symbol??"",@"^(TX[A-Z0-9])([0-9]+)([A-X])([0-9])$");if(!match.Success)return null;
  char monthCode=match.Groups[3].Value[0];string side=monthCode<='L'?"call":"put";
  string normalized=(callPut??"").Trim().ToUpperInvariant();if(normalized=="C"||normalized=="CALL")side="call";else if(normalized=="P"||normalized=="PUT")side="put";
  if(double.IsNaN(strike)||double.IsInfinity(strike)||strike<=0)if(!double.TryParse(match.Groups[2].Value,NumberStyles.None,CultureInfo.InvariantCulture,out strike)||strike<=0)return null;
  DateTime expiry;string[] formats={"yyyyMMdd","yyyy-MM-dd","yyyy/MM/dd"};bool exact=DateTime.TryParseExact((maturity??"").Trim(),formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out expiry);
  if(!exact){int year=DateTime.UtcNow.AddHours(8).Year/10*10+(match.Groups[4].Value[0]-'0');if(year<DateTime.UtcNow.AddHours(8).Year-1)year+=10;int month=monthCode<='L'?monthCode-'A'+1:monthCode-'M'+1;expiry=new DateTime(year,month,1);}
  string label=expiry.ToString(exact?"yyyy-MM-dd":"yyyy-MM",CultureInfo.InvariantCulture),prefix=match.Groups[1].Value;
  return new OptionDescriptor(){Symbol=symbol,Group=prefix+" · "+label,Prefix=prefix,Expiry=label,Side=side,Strike=strike,ExpiryDate=exact?expiry:expiry.AddMonths(1).AddDays(-1)};
 }
}
class OptionLeg {
 public readonly OptionDescriptor Contract;
 public double Bid,Ask,Last;public int BidQty,AskQty,LastQty;public long QuoteReceipt,TradeReceipt,Volume;
 public bool Subscribed;public string Error="";
 public OptionLeg(OptionDescriptor descriptor){Contract=descriptor;}
}
class OptionChain {
 readonly object gate=new object();readonly Dictionary<string,OptionLeg> legs=new Dictionary<string,OptionLeg>(StringComparer.Ordinal);
 public string Group { get; private set; } public string State { get; private set; }
 public OptionChain(){Group="";State="尚未訂閱選擇權";}
 public static List<OptionDescriptor> Select(IEnumerable<OptionDescriptor> candidates,double reference){
  var pairs=candidates.GroupBy(c=>c.Strike).Where(g=>g.Any(c=>c.Side=="call")&&g.Any(c=>c.Side=="put"));
  return pairs.OrderBy(g=>Math.Abs(g.Key-reference)).ThenBy(g=>g.Key).Take(9).OrderByDescending(g=>g.Key).SelectMany(g=>new[]{g.Where(c=>c.Side=="call").OrderBy(c=>c.Symbol,StringComparer.Ordinal).First(),g.Where(c=>c.Side=="put").OrderBy(c=>c.Symbol,StringComparer.Ordinal).First()}).ToList();
 }
 public void Plan(string group,IEnumerable<OptionDescriptor> contracts){lock(gate){Group=group;legs.Clear();foreach(var c in contracts)legs.Add(c.Symbol,new OptionLeg(c));State=legs.Count==0?"此組別沒有完整 CALL／PUT 配對":"訂閱已規劃，等待報價與成交";}}
 public void Subscription(string symbol,bool success){lock(gate){OptionLeg leg;if(!legs.TryGetValue(symbol,out leg))return;leg.Subscribed=success;leg.Error=success?"":"API 未接受訂閱";State=legs.Values.Any(x=>x.Error.Length>0)?"部分訂閱未被 API 接受，請檢查權限與訂閱上限":"訂閱已送出，等待資料（不代表已收到）";}}
 public bool Book(string symbol,double bid,double ask,int bidQty,int askQty,long time){lock(gate){OptionLeg leg;if(!legs.TryGetValue(symbol,out leg))return false;if(double.IsNaN(bid)||double.IsInfinity(bid)||double.IsNaN(ask)||double.IsInfinity(ask))return true;leg.Bid=bid;leg.Ask=ask;leg.BidQty=bidQty;leg.AskQty=askQty;leg.QuoteReceipt=time;return true;}}
 public bool Trade(string symbol,double price,int qty,long time){lock(gate){OptionLeg leg;if(!legs.TryGetValue(symbol,out leg))return false;leg.Last=price;leg.LastQty=qty;leg.Volume+=qty;leg.TradeReceipt=time;return true;}}
 public string Kind(string symbol){lock(gate){OptionLeg leg;return legs.TryGetValue(symbol,out leg)?leg.Contract.Side:"option";}}
 public string Aggressor(string symbol,double price,long time){lock(gate){OptionLeg leg;if(!legs.TryGetValue(symbol,out leg)||leg.QuoteReceipt<=0||time<leg.QuoteReceipt||time-leg.QuoteReceipt>3000||leg.Bid<=0||leg.Ask<=leg.Bid)return "unknown";return price>=leg.Ask?"buy":price<=leg.Bid?"sell":"unknown";}}
 static string Escape(string s){return s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r"," ").Replace("\n"," ");}
 public string Json(){lock(gate){var b=new StringBuilder("{\"series\":\"");b.Append(Escape(Group)).Append("\",\"status\":\"").Append(Escape(State)).Append("\",\"legs\":[");int i=0;foreach(var leg in legs.Values.OrderByDescending(l=>l.Contract.Strike).ThenBy(l=>l.Contract.Side)){if(i++>0)b.Append(',');b.Append("{\"symbol\":\"").Append(Escape(leg.Contract.Symbol)).Append("\",\"side\":\"").Append(leg.Contract.Side).Append("\",\"strike\":").Append(leg.Contract.Strike.ToString(CultureInfo.InvariantCulture)).Append(",\"bid\":").Append(leg.Bid.ToString(CultureInfo.InvariantCulture)).Append(",\"ask\":").Append(leg.Ask.ToString(CultureInfo.InvariantCulture)).Append(",\"last\":").Append(leg.Last.ToString(CultureInfo.InvariantCulture)).Append(",\"lastQty\":").Append(leg.LastQty).Append(",\"volume\":").Append(leg.Volume).Append(",\"quoteReceipt\":").Append(leg.QuoteReceipt).Append(",\"tradeReceipt\":").Append(leg.TradeReceipt).Append(",\"subscribed\":").Append(leg.Subscribed?"true":"false").Append(",\"error\":\"").Append(Escape(leg.Error)).Append("\"}");}return b.Append("]}").ToString();}}
}
