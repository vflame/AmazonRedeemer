using System.Web;
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
        public static T GetChildOfType<T>(this DependencyObject depObj)
    where T : DependencyObject
        {
            if (depObj == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);

                var result = (child as T) ?? GetChildOfType<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        public static async Task<decimal> AuthenticateToAmazonAsync(this IWebView view, string username, string password, CancellationToken ct)
        {
            string amazonLoginUrl = @"https://www.amazon.com/gc/redeem/";
            if (view.Source == null)
            {
                await view.WaitPageLoadComplete(() =>
                {
                    view.Source = amazonLoginUrl.ToUri();
                }, ct: ct);
            }



            if (view.Source.ToString().ToLower() != amazonLoginUrl)
            {
                await view.WaitPageLoadComplete(() =>
                {
                    view.Source = amazonLoginUrl.ToUri();
                }, ct: ct);
            }
            else
            {
                view.Reload(true);
            }

            if (view.Source.ToString().ToLower().Contains("signin"))
            {
                view.ExecuteJavascript(string.Format("document.querySelector(\"#ap_email\").value=\"{0}\"", username));
                view.ExecuteJavascript(string.Format("document.querySelector(\"#ap_password\").value=\"{0}\"", password));

                await view.WaitPageLoadComplete(() =>
                {
                    view.ExecuteJavascript("document.querySelector(\"#signInSubmit-input\").click()");
                }, 10);

                while (true)
                {
                    await Task.Delay(250, ct);
                    ct.ThrowIfCancellationRequested();
                    if (view.Source.ToString().ToLower().Contains("/redeem/"))
                    {
                        break;
                    }
                }
            }

            return await view.GetAmazonBalanceAsync();

        }

        public static async Task<decimal> GetAmazonBalanceAsync(this IWebView view)
        {
            decimal balance = -1;
            string amazonBalance = "";
            string amazonBalanceUrl = "https://www.amazon.com/gc/redeem/";
            if (view.Source.ToString().ToLower().Contains(amazonBalanceUrl) == false)
            {
                await view.WaitPageLoadComplete(() =>
                {
                    view.Source = amazonBalanceUrl.ToUri();
                });
            }

            amazonBalance = view.ExecuteJavascriptWithResult("document.querySelector('#gc-current-balance').innerHTML").ToString();

            decimal.TryParse(amazonBalance.Replace("$", ""), out balance);

            return balance;




        }



        public static async Task<decimal> ValidateAmazonAsync(this IWebView view, string username, string password, string claimcode, CancellationToken ct)
        {
            Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).browserPlaceHolder.GetChildOfType<System.Windows.Controls.Canvas>().Background = Brushes.Gray; });


            string amazonValidateInUrl = "https://www.amazon.com/gc/redeem/";

            if (view.Source.ToString().ToLower().Contains(amazonValidateInUrl) == false)
            {
                await view.WaitPageLoadComplete(() =>
                {
                    view.Source = amazonValidateInUrl.ToUri();
                }, ct: ct);
            }

            await view.WaitPageLoadComplete(() =>
            {
                view.Reload(false);
            }, ct: ct);


            view.ExecuteJavascript(string.Format("document.querySelector('#gc-redemption-input').value='{0}'", claimcode));

            await Task.Delay(250);


            view.ExecuteJavascript("document.querySelector('#gc-redemption-check-value input').click()");

            int milliSecToWait = 10000;
            int milliSecWaited = 0;


            string gcValue;
            bool codeError = false;
            bool captchaError = false;
            decimal parsedValue = -1;

            while (true)
            {
                if (milliSecWaited > milliSecToWait)
                {
                    break;
                }
                await Task.Delay(500);
                milliSecWaited += 500;


                gcValue = HttpUtility.HtmlDecode(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-check-value-heading').innerHTML").ToString());
                codeError = bool.Parse(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-error') ? (document.querySelector('#gc-redemption-error').innerHTML.toLowerCase().indexOf('claim code is invalid')>-1 ? true : false) : false").ToString());
                captchaError = bool.Parse(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-captcha') ? (document.querySelector('#gc-redemption-captcha').innerHTML.toLowerCase().indexOf('security verification')>-1 ? true : false) : false").ToString());

                if (codeError)
                {
                    break;
                }

                if (captchaError)
                {
                    //  Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).browserPlaceHolder.IsEnabled = true; });
                    // Application.Current.MainWindow.Dispatcher.Invoke(() => { MessageBox.Show("Captcha Detected. Enter Captcha and manually click 'check'."); });


                    while (true)
                    {

                        await Task.Delay(2000);
                        gcValue = HttpUtility.HtmlDecode(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-check-value-heading').innerHTML").ToString());
                        codeError = bool.Parse(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-error') ? (document.querySelector('#gc-redemption-error').innerHTML.toLowerCase().indexOf('claim code is invalid')>-1 ? true : false) : false").ToString());
                        if (codeError)
                        {
                            Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).txtCaptchaWarning.Text = ""; });
                            return parsedValue;
                        }


                        if (gcValue == null || gcValue == "undefined" || gcValue == "null" || gcValue == "")
                        {
                            Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).browserPlaceHolder.GetChildOfType<System.Windows.Controls.Canvas>().Background = null; });

                            Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).txtCaptchaWarning.Text = "Captcha Detected. Enter Captcha and manually click 'check'."; });

                            continue;

                        }

                        string gcParsedClaimCode = HttpUtility.HtmlDecode(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-check-value-result-box span').innerHTML").ToString()).Split()[2].ToLower();

                        if (gcParsedClaimCode == claimcode.Replace("-", "").ToLower())
                        {
                            decimal.TryParse(gcValue.Split()[0].Replace("$", ""), out parsedValue);
                            Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).txtCaptchaWarning.Text = ""; });
                            return parsedValue;

                        }
                        else
                        {
                            Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).browserPlaceHolder.GetChildOfType<System.Windows.Controls.Canvas>().Background = null; });

                            Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).txtCaptchaWarning.Text = "Captcha Detected. Enter Captcha and manually click 'check'."; });


                            continue;
                        }
                    }


                }


                if (gcValue == null || gcValue == "undefined" || gcValue == "null" || gcValue == "")
                {
                    continue;

                }


                else
                {


                    gcValue = HttpUtility.HtmlDecode(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-check-value-heading').innerHTML").ToString());

                    string gcParsedClaimCode = HttpUtility.HtmlDecode(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-check-value-result-box span').innerHTML").ToString()).Split()[2].ToLower();

                    if (gcParsedClaimCode == claimcode.Replace("-", "").ToLower())
                    {
                        decimal.TryParse(gcValue.Split()[0].Replace("$", ""), out parsedValue);
                        return parsedValue;

                    }
                    else
                    {
                        continue;
                    }




                }





            }



            return parsedValue;
        }



        public static async Task<decimal> RedeemAmazonAsync(this IWebView view, string claimcode, CancellationToken ct)
        {
            string amazonCashInUrl = "https://www.amazon.com/gc/redeem/";

            if (view.Source.ToString().ToLower().Contains(amazonCashInUrl) == false)
            {
                await view.WaitPageLoadComplete(() =>
                {
                    view.Source = amazonCashInUrl.ToUri();
                }, ct: ct);
            }
            else
            {
                await view.WaitPageLoadComplete(() =>
                {
                    view.Reload(false);
                }, ct: ct);

            }

            view.ExecuteJavascript(string.Format("document.querySelector('#gc-redemption-input').value='{0}'", claimcode));

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

        CancellationTokenSource CTSRedeemAmazon;

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




            txtExpectedValue.Text = colParsedAmazonGiftCodes.Sum(p => p.Value).ToString();

            if (colParsedAmazonGiftCodes.Count < 1)
            {
                txtUnparsedAmazonCodes.Background = Brushes.Pink;
            }
            else
            {

                txtUnparsedAmazonCodes.Background = Brushes.White;
            }
            //txtExpectedValue.Focus();
            //txtExpectedValue.SelectAll();



        }

        public static readonly string ValidIpAddressRegex = @"(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)";

        private bool ValidateInput()
        {
            txtExpectedValue.Background = Brushes.White;
            txtUnparsedAmazonCodes.Background = Brushes.White;
            txtUsername.Background = Brushes.White;
            txtPassword.Background = Brushes.White;
            bool hasUsername = false;
            bool hasPassword = false;
            bool areCodesParsed = false;
            bool isExecptedValueParsed = false;

            if(txtUsername.Text.Length>0)
            {
                hasUsername = true;
                
            }
            else
            {
                txtUsername.Background = Brushes.Pink;
            }

            if (txtPassword.Password.Length > 0)
            {
                hasPassword = true;

            }
            else
            {
                txtPassword.Background = Brushes.Pink;
            }

            if (colParsedAmazonGiftCodes.Count < 1)
            {
                txtUnparsedAmazonCodes.Background = Brushes.Pink;
                    }
            else
            {

                areCodesParsed = true;
            }

            decimal expectedVal;
            if (decimal.TryParse(txtExpectedValue.Text, out expectedVal))
            {
                if (expectedVal > 0)
                {
                    isExecptedValueParsed = true;

                }
                else
                {
                    txtExpectedValue.Background = Brushes.Pink;
                }
            }
            else
            {
              
                txtExpectedValue.Background = Brushes.Pink;
            }

            if (areCodesParsed && isExecptedValueParsed && hasUsername && hasPassword)
            {
                return true;
            }
            else
            {
                return false;
            }


        }

        private async void btnRedeem_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput() == false)
            {
                return;
            }


            browserPlaceHolder.Children.Clear();
            browser = new Awesomium.Windows.Controls.WebControl();
            browser.Height = browserPlaceHolder.Height;
            browser.Width = browserPlaceHolder.Width;


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
            btnValidate.IsEnabled = false;

            //await browser.WaitPageLoadComplete(() =>
            //{
            //    browser.Source = "amazon.com".ToUri();
            //});




            CTSRedeemAmazon = new CancellationTokenSource();
            decimal currentBalance = await browser.AuthenticateToAmazonAsync(txtUsername.Text, txtPassword.Password, CTSRedeemAmazon.Token);



            if (currentBalance > -1)
            {

                lblBalance.Content = string.Format("Balance: {0}", currentBalance.ToString("C"));

                int gcCount = 0;


                decimal previousBalance = currentBalance;

                decimal startingBalance = currentBalance;

                foreach (AmazonGiftCode gc in colParsedAmazonGiftCodes)
                {
                    try
                    {
                        gcCount++;


                        currentBalance = await browser.RedeemAmazonAsync(gc.Code, CTSRedeemAmazon.Token);

                        lblBalance.Content = string.Format("Balance: {0}", currentBalance.ToString("C"));

                        if (currentBalance > previousBalance)
                        {
                            gc.Redeemed = true;
                            gc.Validated = true;
                            gc.Value = (currentBalance - previousBalance);
                            previousBalance = currentBalance;



                        }
                        else
                        {
                            gc.Redeemed = false;

                        }
                    }
                    catch (OperationCanceledException ex)
                    {

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




        public class AmazonGiftCode : INotifyPropertyChanged
        {
            private string _code;
            private int _id;
            private bool? _redeemed = null;
            private decimal? _value;
            private bool? _validated = null;
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

            public bool? Validated
            {
                get
                {
                    return _validated;
                }

                set
                {
                    _validated = value;
                    NotifyPropertyChanged("Validated");
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

        public sealed class TrulyObservableCollection<T> : ObservableCollection<T> where T : INotifyPropertyChanged
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

        private async void btnValidate_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput() == false)
            {
                return;
            }

            browserPlaceHolder.Children.Clear();
            browser = new Awesomium.Windows.Controls.WebControl();
            browser.Height = browserPlaceHolder.Height;
            browser.Width = browserPlaceHolder.Width;


            var overlay = new System.Windows.Controls.Canvas() { Height = browserPlaceHolder.Height, Width = browserPlaceHolder.Width };
            overlay.Opacity = .5;
            overlay.Background = Brushes.Gray;
            //overlay.IsHitTestVisible = false;



            browserPlaceHolder.Children.Add(browser);
            browserPlaceHolder.Children.Add(overlay);


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

            btnValidate.IsEnabled = false;
            btnRedeem.IsEnabled = false;

            //await browser.WaitPageLoadComplete(() =>
            //{
            //    browser.Source = "amazon.com".ToUri();
            //});




            CTSRedeemAmazon = new CancellationTokenSource();
            decimal currentBalance = await browser.AuthenticateToAmazonAsync(txtUsername.Text, txtPassword.Password, CTSRedeemAmazon.Token);



            if (currentBalance > -1)
            {

                lblBalance.Content = string.Format("Balance: {0}", currentBalance.ToString("C"));

                int gcCount = 0;


                decimal validationBalance = 0;


                // decimal currentGCValidationValue = currentBalance;

                int validationSuccessCount = 0;
                int validationFailureCount = 0;

                foreach (AmazonGiftCode gc in colParsedAmazonGiftCodes)
                {
                    gcCount++;
                    try
                    {
                        decimal currentGCValidationValue = await browser.ValidateAmazonAsync(txtUsername.Text, txtPassword.Password, gc.Code, CTSRedeemAmazon.Token);

                        if (currentGCValidationValue > 0)
                        {
                            if (gc.Value == null)
                            {
                                gc.Value = currentGCValidationValue;

                                gc.Validated = true;
                                validationSuccessCount++;
                                validationBalance += currentGCValidationValue;

                            }
                            else
                            {
                                if (gc.Value == currentGCValidationValue)
                                {
                                    gc.Validated = true;
                                    validationSuccessCount++;
                                    validationBalance += currentGCValidationValue;
                                }
                                else
                                {
                                    gc.Validated = false;
                                    validationFailureCount++;
                                }
                            }


                        }
                        else
                        {
                            gc.Validated = false;
                            validationFailureCount++;
                        }


                    }
                    catch (OperationCanceledException ex)
                    {

                    }







                }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.Format("Successful Validations: {0}/{1}", validationSuccessCount, colParsedAmazonGiftCodes.Count()));
                sb.AppendLine(string.Format("Failed Validations: {0}/{1}", validationFailureCount, colParsedAmazonGiftCodes.Count()));
                sb.Append(string.Format("Validated Balance: ${0}", validationBalance));
                txtResults.Text = sb.ToString();
            }
            else
            {
                MessageBox.Show("Authentication failure.");
            }

            btnRedeem.IsEnabled = true;
            btnValidate.IsEnabled = true;
        }
    }
}
