using HtmlAgilityPack;
using Microsoft.Exchange.WebServices.Data;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NavListener_V2.Models;

namespace NavListener_V2
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly string _exchangeServer;
        private readonly string _username;
        private readonly string _password;
        private ExchangeService exchangeService;
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _exchangeServer = _configuration["Config:URL"];
            _username = _configuration["Config:Username"];
            _password = _configuration["Config:Password"];

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(Path.Combine(_configuration["Config:LOGPATH"], $"logs-{DateTime.Now:yyyy-MM-dd}.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();
            var loggerFactory = new LoggerFactory().AddSerilog();
            _logger = loggerFactory.CreateLogger<Worker>();
            _connectionString = _configuration.GetConnectionString("OracleConnection");
        }

        protected override async System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
        {

            _logger.LogInformation("=============================================================================================================");

            _logger.LogInformation("Nav Mail Listener V 2.3.2 HTML Tags removed  format Worker started.for user {user}", _configuration["Config:Username"]);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                _logger.LogInformation("=============================================================================================================");


                try
                {
                    exchangeService = new ExchangeService(ExchangeVersion.Exchange2016);
                    //exchangeService.Timeout = System.Threading.Timeout.Infinite;
                    exchangeService.Credentials = new WebCredentials(_username, _password);
                    exchangeService.Url = new Uri(_exchangeServer);

                    // Subscribe to new email notifications
                    var streamingSubscription = exchangeService.SubscribeToStreamingNotifications(
                    new FolderId[] { WellKnownFolderName.Inbox }, stoppingToken, new EventType[] { EventType.NewMail });




                    // Create a connection and add the subscription
                    var connection = new StreamingSubscriptionConnection(exchangeService, 30);
                    connection.AddSubscription(await streamingSubscription);

                    // Register event handlers
                    connection.OnNotificationEvent += Connection_OnNotificationEvent;
                    connection.OnDisconnect += Connection_OnDisconnect;
                    connection.Open();

                    // Keep the worker running until cancellation is requested
                    while (!stoppingToken.IsCancellationRequested)
                    {

                        await System.Threading.Tasks.Task.Delay(25 * 60 * 1000, stoppingToken);
                        _logger.LogInformation("=============================================================================================================");
                        //open connection
                        if (connection.IsOpen)
                        {
                            connection.OnNotificationEvent -= Connection_OnNotificationEvent;
                            connection.OnDisconnect -= Connection_OnDisconnect;
                            connection.Close();
                            // Open Connection
                            connection.OnNotificationEvent += Connection_OnNotificationEvent;
                            connection.OnDisconnect += Connection_OnDisconnect;
                            connection.Open();
                            _logger.LogInformation("Connection refreshed ...");
                            _logger.LogInformation("=============================================================================================================");
                        }
                        else
                        {
                            // Open Connection
                            connection.OnNotificationEvent += Connection_OnNotificationEvent;
                            connection.OnDisconnect += Connection_OnDisconnect;
                            connection.Open();
                            _logger.LogInformation("Connection Opened...");
                            _logger.LogInformation("=============================================================================================================");
                        }

                    }

                    // Close the connection
                    connection.Close();
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogInformation("=============================================================================================================");
                    _logger.LogError(ex, "Service stopped for the following reason.");
                    _logger.LogInformation("=============================================================================================================");
                }
                catch (TimeoutException)
                {
                    // Connection timed out, initiate reconnection
                    _logger.LogInformation("=============================================================================================================");
                    _logger.LogWarning("Connection to Exchange server timed out. Reconnecting...");
                    _logger.LogInformation("=============================================================================================================");

                    // Close the connection

                    // Reconnect to the Exchange server
                    await ExecuteAsync(stoppingToken);

                    // Break out of the loop to re-establish the connection
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("=============================================================================================================");
                    _logger.LogError(ex, "An error occurred while processing emails.");
                    _logger.LogInformation("=============================================================================================================");
                }

            }
        }

        private async void Connection_OnNotificationEvent(object sender, NotificationEventArgs args)
        {
            foreach (var notification in args.Events)
            {
                if (notification is ItemEvent itemEvent)
                {
                    if (itemEvent.EventType == EventType.NewMail)
                    {
                        try
                        {
                            PropertySet propSet = new PropertySet(BasePropertySet.IdOnly, EmailMessageSchema.HasAttachments, EmailMessageSchema.Attachments, EmailMessageSchema.From, EmailMessageSchema.Subject, EmailMessageSchema.Body);
                            var item = await Item.Bind(exchangeService, itemEvent.ItemId, propSet);

                            EmailMessage mail = ((EmailMessage)item);

                            var from = mail.From.Address;
                            var subject = mail.Subject;
                            var body = mail.Body;

                            string[] fromList = _configuration.GetSection("Config:FromList").Get<string[]>();
                            string[] subjects = _configuration.GetSection("Config:Subjects").Get<string[]>();

                            bool inFromList = fromList.Any(fl => from.ToLower().Contains(fl.ToLower()));
                            bool inSubjectList = subjects.Any(s => subject.ToLower().Contains(s.ToLower()));

                            if (from != null)
                            {
                                if (inFromList && inSubjectList)
                                {
                                    var hasPrice = false;
                                    var bodyLines = RemoveHtmlTags(body.ToString());
                                    foreach (var line in bodyLines)
                                    {
                                        if (line.Trim().ToLower().Contains(_configuration["Config:Search"].ToLower()))
                                        {
                                            hasPrice = true;
                                            break;
                                        }
                                    }


                                    if (hasPrice)
                                    {
                                        string PricePattern = _configuration["Config:DECIMALREGX"];
                                        string DatePattern = _configuration["Config:DATE1REGEX"];
                                        //string DatePattern2 = _configuration["Config:DATE2REGEX"];
                                        //string DatePattern3 = _configuration["Config:DATE3REGEX"];


                                        string Price = string.Empty;
                                        string Date = string.Empty;

                                        foreach (var line in bodyLines)
                                        {

                                            Match PriceMatch = Regex.Match(line, PricePattern);
                                            Price = PriceMatch.Success ? Price = PriceMatch.Value : string.Empty;
                                            Match DateMatch = Regex.Match(line, DatePattern);
                                            Date = DateMatch.Success ? Date = DateMatch.Value : string.Empty;
                                        }
                                        //Match DateMatch2 = Regex.Match(priceLine, DatePattern2);


                                        if (!string.IsNullOrEmpty(Price) && !string.IsNullOrEmpty(Date))
                                        {
                                            // Get the matched decimal value as a string


                                            // Convert the string to a decimal number
                                            decimal price = decimal.Parse(Price);
                                            DateTime date = new DateTime();
                                            string[] formats = new string[] { "dd-MM-yyyy", "d-M-yyyy" };
                                            DateTime.TryParseExact(Date, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
                                            _logger.LogInformation("Price : " + price);
                                            _logger.LogInformation("Date : " + date);
                                            _logger.LogInformation("From : " + from);
                                            InsertIntoDatabase(price, date, from.ToLower() == "adnan.ahmed@egx.com.eg" ? "ADNAN" : "SERV");
                                        }


                                    }
                                    else
                                    {
                                        _logger.LogInformation("=============================================================================================================");
                                        _logger.LogInformation("Could not find price or date in the coming mail.");
                                        _logger.LogInformation("=============================================================================================================");
                                    }
                                }
                                else if (subject.ToLower().Contains("companydata"))
                                {
                                    OnAttachmentEmailReceived(mail);
                                }
                                else
                                {
                                    _logger.LogInformation("=============================================================================================================");
                                    _logger.LogInformation("Coming mail not recognized its coming from (" + from + ") or the subject not matching subject is (" + subject + ")");
                                    _logger.LogInformation("=============================================================================================================");
                                }
                            }


                        }
                        catch (Exception ex)
                        {
                            _logger.LogInformation("=============================================================================================================");
                            _logger.LogError(ex, "An error occurred while processing a new mail item.");
                            _logger.LogInformation("=============================================================================================================");
                        }
                    }
                }
            }
        }
        private void Connection_OnDisconnect(object sender, SubscriptionErrorEventArgs args)
        {
            _logger.LogError(args.Exception.Message, "Connection to Exchange Server lost. Attempting to reconnect...");
            // Create a new CancellationTokenSource to cancel the previous ExecuteAsync task
            var cancellationTokenSource = new CancellationTokenSource();

            // Start a new task to execute the ExecuteAsync method again
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(50, cancellationTokenSource.Token);
                await ExecuteAsync(cancellationTokenSource.Token);
            }, cancellationTokenSource.Token);
        }
        private void InsertIntoDatabase(decimal price, DateTime date, string user)
        {
            try
            {
                using (OracleConnection connection = new OracleConnection(_connectionString))
                {
                    connection.Open();
                    string sql = "INSERT INTO GOLD_NAV (TRADE_DATE, NAV_VALUE, INSERT_DATE, INSERT_USER) VALUES(:p_date,:p_price,:p_now,:p_user)";
                    using (OracleCommand command = new OracleCommand(sql, connection))
                    {
                        command.Parameters.Add("p_date", OracleDbType.Date).Value = date;
                        command.Parameters.Add("p_price", OracleDbType.Decimal).Value = price;
                        command.Parameters.Add("p_now", OracleDbType.Date).Value = DateTime.Now;
                        command.Parameters.Add("p_user", OracleDbType.Varchar2).Value = user;

                        int rowsInserted = command.ExecuteNonQuery();

                        if (rowsInserted > 0)
                        {
                            Console.WriteLine("Data inserted successfully");
                            _logger.LogInformation("Data inserted successfully");
                        }
                        else
                        {
                            Console.WriteLine("No rows inserted");
                            _logger.LogInformation("No rows inserted");
                        }
                    }

                    connection.Close();
                }
                _logger.LogInformation("=============================================================================================================");
                _logger.LogInformation($"Inserted values into the database: price = {price}, date = {date}");
                _logger.LogInformation("=============================================================================================================");
            }
            catch (Exception ex)
            {
                _logger.LogInformation("=============================================================================================================");
                _logger.LogError(ex, "An error occurred while inserting data to database. error message ");
                _logger.LogInformation("=============================================================================================================");
            }
        }
        public static List<string> GetStringsBetween(string input, string startString, string endString)
        {
            List<string> result = new List<string>();
            int startIndex = 0;

            while (startIndex != -1)
            {
                startIndex = input.IndexOf(startString, startIndex);
                if (startIndex == -1)
                    break;

                startIndex += startString.Length;

                int endIndex = input.IndexOf(endString, startIndex);
                if (endIndex == -1)
                    break;

                string value = input.Substring(startIndex, endIndex - startIndex);
                result.Add(value);

                startIndex = endIndex + endString.Length;
            }

            return result;
        }
        public string[] RemoveHtmlTags(string htmlString)
        {
            // Load the HTML string into the HtmlDocument object
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(htmlString);

            // Remove all script tags
            var scriptNodes = htmlDoc.DocumentNode.SelectNodes("//script");
            if (scriptNodes != null)
            {
                foreach (var node in scriptNodes)
                {
                    node.Remove();
                }
            }

            // Remove all style tags
            var styleNodes = htmlDoc.DocumentNode.SelectNodes("//style");
            if (styleNodes != null)
            {
                foreach (var node in styleNodes)
                {
                    node.Remove();
                }
            }

            // Remove all HTML tags and keep only the inner text
            StringBuilder sb = new StringBuilder();
            foreach (var node in htmlDoc.DocumentNode.DescendantsAndSelf())
            {
                if (!node.HasChildNodes && node.NodeType == HtmlNodeType.Text)
                {
                    sb.Append(node.InnerText);
                }
            }

            string plainText = sb.ToString().Trim();

            // Split the plain text into an array of strings
            string[] plainTextArray = plainText.Split(new string[] { ":&nbsp;" }, StringSplitOptions.RemoveEmptyEntries);

            // Remove &nbsp; from each string in the array
            for (int i = 0; i < plainTextArray.Length; i++)
            {
                plainTextArray[i] = plainTextArray[i].Replace("&nbsp;", "");
            }

            return plainTextArray;
        }
        private void OnAttachmentEmailReceived(EmailMessage e)
        {
            if (e.HasAttachments)
            {
                foreach (var attachment in e.Attachments)
                {
                    if (attachment is FileAttachment fileAttachment && fileAttachment.Name.EndsWith(".csv"))
                    {

                        ProcessCsvAttachment(fileAttachment);
                    }
                }
            }
        }
        private async void ProcessCsvAttachment(FileAttachment csvAttachment)
        {
            // Load the CSV file content
            await csvAttachment.Load();

            // Read the CSV data
            using (var stream = new MemoryStream(csvAttachment.Content))
            {
                using (var reader = new StreamReader(stream))
                {
                    string headerLine = reader.ReadLine();
                    string[] headers = headerLine.Split(';');

                    while (!reader.EndOfStream)
                    {
                        string dataLine = reader.ReadLine();
                        string[] values = dataLine.Split(';');
                        CompanyGoldData Data = MapObject(headers, values);
                        // Parse the data and map it to your object
                        //YourObject obj = MapToYourObject(dataLine);

                        // Do something with the mapped object
                        Console.WriteLine($"Mapped object: Correctly ");
                    }
                }

            }
        }

        private CompanyGoldData MapObject(string[] headerLine, string[] line)
        {
            CompanyGoldData Data = new CompanyGoldData();
            for (int i = 0; i < line.Length; i++)
            {
                switch (headerLine[i])
                {
                    case var header when header.Contains("COMP_NAME_ENG"):
                        Data.CompanyName = line[i];
                        break;
                    case var header when header.Contains("COMP_NAME_ARB"):
                        Data.CompanyNameAr = line[i];
                        break;
                    case var header when header.Contains("TRADE_DATE"):
                        {
                            if (DateTime.TryParse(line[i], out DateTime dateValue))
                            {
                                Data.TradeDate = dateValue;
                            }
                            break;
                        }

                    case var header when header.Contains("BUY_PRICE"):
                        Data.BuyPrice = Convert.ToDecimal(line[i]);
                        break;
                    case var header when header.Contains("BUY_QTY"):
                        Data.BuyQty = Convert.ToDecimal(line[i]);
                        break;
                    case var header when header.Contains("SELL_PRICE"):
                        Data.SellPrice = Convert.ToDecimal(line[i]);
                        break;
                    case var header when header.Contains("SELL_QTY"):
                        Data.SellQty = Convert.ToDecimal(line[i]);
                        break;
                    case var header when header.Contains("BUY_VALUE"):
                        Data.BuyValue = Convert.ToDecimal(line[i]);
                        break;
                    case var header when header.Contains("SELL_VALUE"):
                        Data.SellValue = Convert.ToDecimal(line[i]);
                        break;
                    case var header when header.Contains("NAV_PRICE"):
                        Data.NavPrice = Convert.ToDecimal(line[i]);
                        break;

                }
            }
            return Data;
        }
    }
}