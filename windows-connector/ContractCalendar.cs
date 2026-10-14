using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
class MonthlyContract {
 public string Symbol,Prefix,Month;
 public DateTime Expiry;
 public static MonthlyContract Parse(string symbol,string maturity,DateTime taipei){var m=Regex.Match(symbol??"",@"^(TXF|MXF|TMF)([A-L])([0-9])$");if(!m.Success)return null;int year=taipei.Year/10*10+(m.Groups[3].Value[0]-'0');if(year<taipei.Year-1)year+=10;int month=m.Groups[2].Value[0]-'A'+1;DateTime expiry;string[] formats={"yyyyMMdd","yyyy-MM-dd","yyyy/MM/dd"};if(!DateTime.TryParseExact(maturity??"",formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out expiry)||expiry.Year!=year||expiry.Month!=month){var first=new DateTime(year,month,1);expiry=first.AddDays(((int)DayOfWeek.Wednesday-(int)first.DayOfWeek+7)%7+14);}return new MonthlyContract(){Symbol=symbol,Prefix=m.Groups[1].Value,Month=year.ToString("0000")+month.ToString("00"),Expiry=expiry.Date.AddHours(13).AddMinutes(30)};}
 public static MonthlyContract Choose(IEnumerable<MonthlyContract> contracts,DateTime taipei,bool near){return contracts.Where(c=>c!=null&&c.Prefix=="TXF"&&c.Expiry>=taipei&&(near||c.Month==taipei.ToString("yyyyMM"))).OrderBy(c=>c.Expiry).FirstOrDefault();}
 public static string SymbolFor(string prefix,string month){int year=int.Parse(month.Substring(0,4)),m=int.Parse(month.Substring(4,2));return prefix+(char)('A'+m-1)+(year%10);}
 public static string MonthFor(string symbol,DateTime taipei){var f=Parse(symbol,"",taipei);if(f!=null)return f.Month;var o=Regex.Match(symbol??"",@"^TX[A-Z0-9][0-9]+([A-X])([0-9])$");if(!o.Success)return "";int year=taipei.Year/10*10+(o.Groups[2].Value[0]-'0');if(year<taipei.Year-1)year+=10;char c=o.Groups[1].Value[0];int month=c<='L'?c-'A'+1:c-'M'+1;return year.ToString("0000")+month.ToString("00");}
}
