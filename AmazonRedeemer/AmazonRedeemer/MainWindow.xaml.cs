using Awesomium;
using Awesomium.Core;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.IO;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Net.Mail;
using System.Collections.Specialized;

namespace AmazonRedeemer
{
    public static class Extensions
    {

        public static async Task<decimal> AuthenticateAmazonAsync(this IWebView view, string username, string password, CancellationToken ct)
        {

            string amazonLoginUrl = @"https://www.amazon.com/gc/redeem/ref=gc_redeem_new_exp";

            await view.WaitPageLoadComplete(() =>
            {
                view.Source = amazonLoginUrl.ToUri();
            }, ct: ct);

            view.ExecuteJavascript(string.Format("document.querySelector(\"#ap_email\").value=\"{0}\"", username));
            view.ExecuteJavascript(string.Format("document.querySelector(\"#ap_password\").value=\"{0}\"", password));

            await view.WaitPageLoadComplete(() =>
            {
                view.ExecuteJavascript("document.querySelector(\"#signInSubmit-input\").click()");
            });

            while (true)
            {
                await Task.Delay(1000, ct);
                ct.ThrowIfCancellationRequested();
                if (view.Source.ToString().Contains("/redeem/"))
                {
                    break;
                }
            }

            return (await view.GetAmazonBalanceAsync());

        }

        public static async Task<decimal> GetAmazonBalanceAsync(this IWebView view)
        {
            decimal balance = -1;
            string amazonBalance = "";
            string amazonBalanceUrl = "https://www.amazon.com/gc/redeem/ref=gc_redeem_new_exp";
            if (view.Source.ToString().Contains(amazonBalanceUrl) == false)
            {
                await view.WaitPageLoadComplete(() =>
                {
                    view.Source = amazonBalanceUrl.ToUri();
                });
            }

            amazonBalance = view.ExecuteJavascriptWithResult("document.querySelector(\"#gc-current-balance\").innerHTML").ToString();

            decimal.TryParse(amazonBalance.Replace("$", ""), out balance);

            return balance;




        }

        public static async Task<decimal> RedeemAmazonAsync(this IWebView view, string username, string password, string claimcode, CancellationToken ct)
        {
            string amazonCashInUrl = "https://www.amazon.com/gc/redeem/ref=gc_redeem_new_exp";

            if (view.Source != amazonCashInUrl.ToUri())
            {
                await view.WaitPageLoadComplete(() =>
                {
                    view.Source = amazonCashInUrl.ToUri();
                }, ct: ct);
            }

            view.ExecuteJavascript(string.Format("document.querySelector(\"#gc-redemption-input\").value=\"{0}\"", claimcode));

            await view.WaitPageLoadComplete(() =>
            {
                //view.ExecuteJavascript("document.querySelector(\"input[name=applytoaccount]\").click()");
                view.ExecuteJavascript("document.querySelector(\"#gc-redemption-apply input\").click()");
            }, ct: ct);



            return await view.GetAmazonBalanceAsync();
        }
        public static Uri ToUri(this string s)
        {
            return new UriBuilder(s).Uri;
        }
        public static async Task<bool> WaitPageLoadComplete(this IWebView view, Action TriggerPageChangeAction, int timeOutSec = 0, CancellationToken ct = new CancellationToken())
        {



            SemaphoreSlim signal = new SemaphoreSlim(0, 1);
            bool result = false;
            FrameEventHandler frameEvent = (o, e) =>
            {
                if (e.IsMainFrame)
                {
                    result = true;

                    signal.Release();
                }
            };
            view.LoadingFrameComplete += frameEvent;

            TriggerPageChangeAction.Invoke();

            if (timeOutSec > 0)
            {
                await signal.WaitAsync(TimeSpan.FromSeconds(timeOutSec), ct);
            }
            else
            {
                await signal.WaitAsync(ct);
            }
            view.LoadingFrameComplete -= frameEvent;
            return result;
        }


    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public TrulyObservableCollection<AmazonGiftCode> colParsedAmazonGiftCodes = new TrulyObservableCollection<AmazonGiftCode>();

        CancellationTokenSource CTSRedeem;

        public MainWindow()
        {

            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;


            if (WebCore.IsInitialized == false)
            {
                WebCore.Initialize(new WebConfig
                {
                    LogLevel = LogLevel.None,
                });
            }


            InitializeComponent();

            //browser.WebSession = WebCore.CreateWebSession(new WebPreferences() { Plugins = false });

            datagridParsedAmazonCodes.ItemsSource = colParsedAmazonGiftCodes;


        }

        Awesomium.Windows.Controls.WebControl browser;

        private void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
        {
            using (StreamWriter sw = new StreamWriter("AmazonRedeemerException.log", true))
            {
                StringBuilder sb = new StringBuilder();

                sb.Append(DateTime.Now);
                sb.Append("\t");
                sb.Append(e.ExceptionObject.ToString());

                sw.WriteLine(sb);
            }
        }
        private void btnParseAmazonCode_Click(object sender, RoutedEventArgs e)
        {

            //string regex = @"USD ([$\d\.]*)[\s]*Amazon.com Gift Card claim code: ([\d\w\-]*)";
            string regex = @"\$([\d\.]*).([\w\d]{4}-[\w\d]{6}-[\w\d]{4})";
            MatchCollection matches = Regex.Matches(txtUnparsedAmazonCodes.Text, regex);
            bool noValue = false;

            if (matches.Count < 1)
            {
                regex = @"([\w\d]{4}-[\w\d]{6}-[\w\d]{4})";
                matches = Regex.Matches(txtUnparsedAmazonCodes.Text, regex);
                noValue = true;
            }


            StringBuilder sb = new StringBuilder();


            colParsedAmazonGiftCodes.Clear();
            int codeCount = 0;
            foreach (Match m in matches)
            {
                codeCount++;
                if (noValue)
                {
                    colParsedAmazonGiftCodes.Add(new AmazonGiftCode(codeCount, m.Groups[1].Value, null));
                }
                else
                {
                    colParsedAmazonGiftCodes.Add(new AmazonGiftCode(codeCount, m.Groups[2].Value, decimal.Parse(m.Groups[1].Value)));
                }
            }


            if (colParsedAmazonGiftCodes.Any(p => p.Value == null) || colParsedAmazonGiftCodes.Count < 1)
            {
                MessageBox.Show("Null $ values detected");
                panelLogin.IsEnabled = false;
            }
            else
            {
                panelLogin.IsEnabled = true;
            }

            txtExpectedValue.Text = colParsedAmazonGiftCodes.Sum(p => p.Value).ToString();
            txtExpectedValue.Focus();
            txtExpectedValue.SelectAll();



        }

        public static readonly string ValidIpAddressRegex = @"(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)";


        private async void btnRedeem_Click(object sender, RoutedEventArgs e)
        {
            browserPlaceHolder.Children.Clear();
            browser = new Awesomium.Windows.Controls.WebControl();
            browser.Height = 508;
            browser.Width = 508;
            

            browserPlaceHolder.Children.Add(browser);
            
          

            if (cbUseSSHProxy.IsChecked == true)
            {
                browser.WebSession = WebCore.CreateWebSession(new WebPreferences()
                {
                    Plugins = false,
                    ProxyConfig = string.Format("socks4://{0}:{1}", txtLocalHost.Text, txtLocalPort.Text)
                });

                await browser.WaitPageLoadComplete(() =>
                {
                    browser.Source = "api.ipify.org".ToUri();
                });

                string ip = Regex.Match(browser.HTML, ValidIpAddressRegex).Value;

                MessageBox.Show(string.Format("Proxy IP: {0}", ip));
            }

            btnRedeem.IsEnabled = false;

            await browser.WaitPageLoadComplete(() =>
            {
                browser.Source = "amazon.com".ToUri();
            });




            CTSRedeem = new CancellationTokenSource();
            decimal currentBalance = await browser.AuthenticateAmazonAsync(txtUsername.Text, txtPassword.Password, CTSRedeem.Token);



            if (currentBalance > -1)
            {

                lblBalance.Content = string.Format("Balance: {0}", currentBalance.ToString("C"));

                int gcCount = 0;


                decimal previousBalance = currentBalance;

                decimal startingBalance = currentBalance;

                foreach (AmazonGiftCode gc in colParsedAmazonGiftCodes)
                {
                    gcCount++;


                    currentBalance = await browser.RedeemAmazonAsync(txtUsername.Text, txtPassword.Password, gc.Code, CTSRedeem.Token);

                    lblBalance.Content = string.Format("Balance: {0}", currentBalance.ToString("C"));

                    if (currentBalance > previousBalance)
                    {
                        gc.Redeemed = true;
                        gc.Value = (currentBalance - previousBalance);
                        previousBalance = currentBalance;



                    }
                    else
                    {
                        gc.Redeemed = false;

                    }



                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.Format("Starting balance: ${0}", startingBalance));
                sb.AppendLine(string.Format("Ending balance: ${0}", currentBalance));
                sb.Append(string.Format("Value redeemed: ${0}", currentBalance - startingBalance));
                txtResults.Text = sb.ToString();
            }
            else
            {
                MessageBox.Show("Authentication failure.");
            }

            btnRedeem.IsEnabled = true;
        }


        private void txtExpectedValue_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            decimal expectedVal;
            if (decimal.TryParse(txtExpectedValue.Text, out expectedVal))
            {
                if (colParsedAmazonGiftCodes.Count > 0)
                {
                    panelLogin.IsEnabled = true;
                }
            }
            else
            {
                panelLogin.IsEnabled = false;
            }
        }

        public class AmazonGiftCode : INotifyPropertyChanged
        {
            private string _code;
            private int _id;
            private bool? _redeemed = null;
            private decimal? _value;
            public AmazonGiftCode(int id, string code, decimal? value)
            {
                _id = id;
                _value = value;
                _code = code;
            }

            public AmazonGiftCode(int id, string code)
            {
                _id = id;
                _code = code;
            }

            public event PropertyChangedEventHandler PropertyChanged;

            public string Code
            {
                get { return _code; }
                set
                {
                    _code = value;
                    NotifyPropertyChanged("Code");
                }
            }

            public int Id
            {
                get { return _id; }
                set
                {
                    _id = value;
                    NotifyPropertyChanged("Id");
                }
            }

            public bool? Redeemed
            {
                get { return _redeemed; }
                set
                {
                    _redeemed = value;
                    NotifyPropertyChanged("Redeemed");
                }
            }
            public decimal? Value
            {
                get { return _value; }
                set
                {
                    _value = value;
                    NotifyPropertyChanged("Value");
                }
            }
            private void NotifyPropertyChanged(String propertyName = "")
            {
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
                }
            }


        }

        public sealed class TrulyObservableCollection<T> : ObservableCollection<T>
    where T : INotifyPropertyChanged
        {
            public TrulyObservableCollection()
            {
                CollectionChanged += FullObservableCollectionCollectionChanged;
            }

            private void FullObservableCollectionCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
            {
                if (e.NewItems != null)
                {
                    foreach (Object item in e.NewItems)
                    {
                        ((INotifyPropertyChanged)item).PropertyChanged += ItemPropertyChanged;
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (Object item in e.OldItems)
                    {
                        ((INotifyPropertyChanged)item).PropertyChanged -= ItemPropertyChanged;
                    }
                }
            }

            private void ItemPropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                NotifyCollectionChangedEventArgs args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, sender, sender, IndexOf((T)sender));
                OnCollectionChanged(args);
            }
        }
    }
}
