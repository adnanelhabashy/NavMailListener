using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NavListener_V2.Models
{
    public class CompanyGoldData
    {
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyNameAr { get; set; } = string.Empty;
        public DateTime TradeDate { get; set; }
        public decimal BuyPrice { get; set; }
        public decimal BuyQty { get; set; }
        public decimal SellPrice { get; set; }
        public decimal SellQty { get; set; }
        public decimal BuyValue { get; set; }
        public decimal SellValue { get; set; }
        public decimal NavPrice { get; set; }
    }
}
