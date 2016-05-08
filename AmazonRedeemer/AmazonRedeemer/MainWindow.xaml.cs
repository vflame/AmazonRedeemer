namespace AmazonRedeemer
{
    using Awesomium.Core;
    using System;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using System.Windows;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    public static class Extensions
    {
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
            string amazonBalance = String.Empty;
            string amazonBalanceUrl = "https://www.amazon.com/gc/redeem/";
            if (view.Source.ToString().ToLower().Contains(amazonBalanceUrl) == false)
            {
                await view.WaitPageLoadComplete(() =>
                {
                    view.Source = amazonBalanceUrl.ToUri();
                });
            }

            amazonBalance = view.ExecuteJavascriptWithResult("document.querySelector('#gc-current-balance').innerHTML").ToString();

            decimal.TryParse(amazonBalance.Replace("$", String.Empty), out balance);

            return balance;
        }

        public static T GetChildOfType<T>(this DependencyObject depObj) where T : DependencyObject
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
                
                view.ExecuteJavascript("document.querySelector(\"#gc-redemption-apply input\").click()");
            }, ct: ct);

            return await view.GetAmazonBalanceAsync();
        }

        private static SemaphoreSlim captchaSolvedSignal = new SemaphoreSlim(0, 1);

        public static Uri ToUri(this string s)
        {
            return new UriBuilder(s).Uri;
        }

        public static async Task<decimal> ValidateAmazonAsync(this IWebView view, string username, string password, string claimcode, CancellationToken ct)
        {
            Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).browserPlaceHolder.GetChildOfType<System.Windows.Controls.Canvas>().Background = System.Windows.Media.Brushes.Gray; });

            string amazonValidateInUrl = "https://www.amazon.com/gc/redeem/";

            if (view.Source.ToString().ToLower().Contains(amazonValidateInUrl) == false)
            {
                await view.WaitPageLoadComplete(() =>
                {
                    view.Source = amazonValidateInUrl.ToUri();
                },
                ct: ct);
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
                    captchaSolvedSignal = new SemaphoreSlim(0, 1);
                    //  Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).browserPlaceHolder.IsEnabled = true; });
                    // Application.Current.MainWindow.Dispatcher.Invoke(() => { MessageBox.Show("Captcha Detected. Enter Captcha and manually click 'check'."); });

                    while (true)
                    {
                        await Task.Delay(2000);
                        gcValue = HttpUtility.HtmlDecode(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-check-value-heading').innerHTML").ToString());
                        codeError = bool.Parse(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-error') ? (document.querySelector('#gc-redemption-error').innerHTML.toLowerCase().indexOf('claim code is invalid')>-1 ? true : false) : false").ToString());
                        if (codeError)
                        {
                            Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).CaptchaPanel.Visibility = Visibility.Hidden; });
                            return parsedValue;
                        }

                        if (gcValue == null || gcValue == "undefined" || gcValue == "null" || gcValue == "")
                        {
                            await Application.Current.MainWindow.Dispatcher.Invoke(async () =>
                             {
                                 ((MainWindow)Application.Current.MainWindow).CaptchaPanel.Visibility = Visibility.Visible;
                                 ((MainWindow)Application.Current.MainWindow).CaptchaImage.Source = await view.GetCaptchaData();
                                 await captchaSolvedSignal.WaitAsync();

                                 await Task.Delay(250);

                                 view.ExecuteJavascript(string.Format("document.querySelector('input[name=\"captchaInput\"]').value='{0}'", ((MainWindow)Application.Current.MainWindow).txtCaptchaResult.Text));

                                 view.ExecuteJavascript("document.querySelector('#gc-redemption-check-value input').click()");
                             });

                            continue;
                        }

                        string gcParsedClaimCode = HttpUtility.HtmlDecode(view.ExecuteJavascriptWithResult("document.querySelector('#gc-redemption-check-value-result-box span').innerHTML").ToString()).Split()[2].ToLower();

                        if (gcParsedClaimCode == claimcode.Replace("-", "").ToLower())
                        {
                            decimal.TryParse(gcValue.Split()[0].Replace("$", ""), out parsedValue);
                            Application.Current.MainWindow.Dispatcher.Invoke(() => { ((MainWindow)Application.Current.MainWindow).CaptchaPanel.Visibility = Visibility.Hidden; });
                            return parsedValue;
                        }
                        else
                        {
                            await Application.Current.MainWindow.Dispatcher.Invoke(async () =>
                            {
                                ((MainWindow)Application.Current.MainWindow).CaptchaPanel.Visibility = Visibility.Visible;
                                ((MainWindow)Application.Current.MainWindow).CaptchaImage.Source = await view.GetCaptchaData();
                                await captchaSolvedSignal.WaitAsync();

                                await Task.Delay(250);
                                view.ExecuteJavascript(string.Format("document.querySelector('input[name=\"captchaInput\"]').value='{0}'", ((MainWindow)Application.Current.MainWindow).txtCaptchaResult.Text));

                                view.ExecuteJavascript("document.querySelector('#gc-redemption-check-value input').click()");
                            });

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

        public static void CaptchaSolved(this IWebView view)
        {
            if (captchaSolvedSignal != null)
            {
                captchaSolvedSignal.Release();
            }
        }

        public static async Task<BitmapImage> GetCaptchaData(this IWebView view)
        {
            int top;
            int left;
            int width;
            int height;

            JSObject obj = view.ExecuteJavascriptWithResult("document.querySelector('.gc-captcha-image').getBoundingClientRect()");

            left = (int)obj["left"];
            width = (int)obj["width"];
            top = (int)obj["top"];
            height = (int)obj["height"];

            byte[] binaryData = new byte[] { };

            BitmapSurface surface = view.Surface as BitmapSurface;
            BitmapSource bitmapSource = null;

            if (surface == null)
            {
                try
                {
                    Awesomium.Windows.Controls.WebViewPresenter presenter = view.Surface as Awesomium.Windows.Controls.WebViewPresenter;
                    bitmapSource = presenter.Image as BitmapSource;
                }
                catch (NullReferenceException ex)
                {
                }
            }

            if (bitmapSource != null)
            {
                using (MemoryStream outStream = new MemoryStream())
                {
                    BitmapEncoder enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bitmapSource));
                    enc.Save(outStream);

                    Rectangle cropRect = new Rectangle(left, top, width, height);
                    Bitmap src = Image.FromStream(outStream) as Bitmap;
                    Bitmap target = new Bitmap(cropRect.Width, cropRect.Height);

                    using (Graphics g = Graphics.FromImage(target))
                    {
                        g.DrawImage(src, new Rectangle(0, 0, target.Width, target.Height),
                                         cropRect,
                                         GraphicsUnit.Pixel);
                    }

                    //target.Save(@"captcha\yourfile.jpg", System.Drawing.Imaging.ImageFormat.Jpeg);

                    using (MemoryStream ms = new MemoryStream())
                    {
                        target.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                        binaryData = ms.ToArray();
                    }
                }
            }

            BitmapImage bitmap = new BitmapImage();

            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(binaryData);
            bitmap.EndInit();

            return bitmap;
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
        public static readonly string ValidIpAddressRegex = @"(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)";
        public TrulyObservableCollection<AmazonGiftCode> colParsedAmazonGiftCodes = new TrulyObservableCollection<AmazonGiftCode>();

        private Awesomium.Windows.Controls.WebControl browser;
        private CancellationTokenSource CTSRedeemAmazon;

        public MainWindow()
        {
            AppDomain.CurrentDomain.UnhandledException += this.UnhandledExceptionTrapper;

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
                    this.colParsedAmazonGiftCodes.Add(new AmazonGiftCode(codeCount, m.Groups[1].Value, null));
                }
                else
                {
                    this.colParsedAmazonGiftCodes.Add(new AmazonGiftCode(codeCount, m.Groups[2].Value, decimal.Parse(m.Groups[1].Value)));
                }
            }

            if (colParsedAmazonGiftCodes.Count < 1)
            {
                txtUnparsedAmazonCodes.Background = System.Windows.Media.Brushes.Pink;
            }
            else
            {
                txtExpectedValue.Text = colParsedAmazonGiftCodes.Sum(p => p.Value).ToString();

                if (txtExpectedValue.Text == String.Empty)
                {
                    txtExpectedValue.Background = System.Windows.Media.Brushes.Pink;
                }
                else
                {
                    decimal expectedValue = 0;
                    decimal.TryParse(txtExpectedValue.Text, out expectedValue);
                    if (expectedValue > 0)
                    {
                        txtExpectedValue.Background = System.Windows.Media.Brushes.White;
                    }
                    else
                    {
                        txtExpectedValue.Background = System.Windows.Media.Brushes.Pink;
                    }
                }

                txtUnparsedAmazonCodes.Background = System.Windows.Media.Brushes.White;
            }
            //txtExpectedValue.Focus();
            //txtExpectedValue.SelectAll();
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
                sb.AppendLine(string.Format("Value redeemed: ${0}", currentBalance - startingBalance));
                txtResults.Text = sb.ToString();
            }
            else
            {
                MessageBox.Show("Authentication failure.");
            }

            btnRedeem.IsEnabled = true;
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
            overlay.Background = System.Windows.Media.Brushes.Gray;

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

            CTSRedeemAmazon = new CancellationTokenSource();
            decimal currentBalance = await browser.AuthenticateToAmazonAsync(txtUsername.Text, txtPassword.Password, CTSRedeemAmazon.Token);

            if (currentBalance > -1)
            {
                lblBalance.Content = string.Format("Balance: {0}", currentBalance.ToString("C"));

                int gcCount = 0;

                decimal validationBalance = 0;

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
                sb.AppendLine(string.Format("Validated Balance: ${0}", validationBalance));
                txtResults.Text = sb.ToString();
            }
            else
            {
                MessageBox.Show("Authentication failure.");
            }

            btnRedeem.IsEnabled = true;
            btnValidate.IsEnabled = true;
        }

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

        private bool ValidateInput()
        {
            txtExpectedValue.Background = System.Windows.Media.Brushes.White;
            txtUnparsedAmazonCodes.Background = System.Windows.Media.Brushes.White;
            txtUsername.Background = System.Windows.Media.Brushes.White;
            txtPassword.Background = System.Windows.Media.Brushes.White;
            bool hasUsername = false;
            bool hasPassword = false;
            bool areCodesParsed = false;
            bool isExecptedValueParsed = false;

            if (txtUsername.Text.Length > 0)
            {
                hasUsername = true;
            }
            else
            {
                txtUsername.Background = System.Windows.Media.Brushes.Pink;
            }

            if (txtPassword.Password.Length > 0)
            {
                hasPassword = true;
            }
            else
            {
                txtPassword.Background = System.Windows.Media.Brushes.Pink;
            }

            if (colParsedAmazonGiftCodes.Count < 1)
            {
                txtUnparsedAmazonCodes.Background = System.Windows.Media.Brushes.Pink;
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
                    txtExpectedValue.Background = System.Windows.Media.Brushes.Pink;
                }
            }
            else
            {
                txtExpectedValue.Background = System.Windows.Media.Brushes.Pink;
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

        public class AmazonGiftCode : INotifyPropertyChanged
        {
            private string _code;
            private int _id;
            private bool? _redeemed = null;
            private bool? _validated = null;
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

        private void btnSubmitCaptchaResult_Click(object sender, RoutedEventArgs e)
        {
            browser.CaptchaSolved();
        }
    }
}