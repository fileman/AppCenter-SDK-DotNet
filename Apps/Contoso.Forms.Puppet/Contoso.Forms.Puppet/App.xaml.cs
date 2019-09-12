// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Auth;
using Microsoft.AppCenter.Crashes;
using Microsoft.AppCenter.Data;
using Microsoft.AppCenter.Distribute;
using Microsoft.AppCenter.Push;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Forms;
using XamarinDevice = Xamarin.Forms.Device;

namespace Contoso.Forms.Puppet
{
    public interface IClearCrashClick
    {
        void ClearCrashButton();
    }

    public partial class App
    {
        public const string LogTag = "AppCenterXamarinPuppet";

        // App Center B2C secrets
        static readonly IReadOnlyDictionary<string, string> B2CAuthAppSecrets = new Dictionary<string, string>
        {
            { XamarinDevice.UWP, "a678b499-1912-4a94-9d97-25b569284d3a" }, // same for all devices
            { XamarinDevice.Android, "bff0949b-7970-439d-9745-92cdc59b10fe" },
            { XamarinDevice.iOS, "b889c4f2-9ac2-4e2e-ae16-dae54f2c5899" }
        };

        // App Center AAD secrets
        static readonly IReadOnlyDictionary<string, string> AADAuthAppSecrets = new Dictionary<string, string>
        {
            { XamarinDevice.UWP, "a678b499-1912-4a94-9d97-25b569284d3a" }, // same for all devices
            { XamarinDevice.Android, "739eacee-42de-454c-b0d7-c093e765e009" },
            { XamarinDevice.iOS, "e9f015c6-6c2b-4410-8053-70eaa52d90e0" }
        };

        public App()
        {
            InitializeComponent();
            MainPage = new NavigationPage(new MainPuppetPage());
        }

        protected override void OnStart()
        {
            if (!AppCenter.Configured)
            {
                AppCenterLog.Assert(LogTag, "AppCenter.LogLevel=" + AppCenter.LogLevel);
                AppCenter.LogLevel = LogLevel.Verbose;
                AppCenterLog.Info(LogTag, "AppCenter.LogLevel=" + AppCenter.LogLevel);
                AppCenterLog.Info(LogTag, "AppCenter.Configured=" + AppCenter.Configured);

                // Set callbacks
                Crashes.ShouldProcessErrorReport = ShouldProcess;
                Crashes.ShouldAwaitUserConfirmation = ConfirmationHandler;
                Crashes.GetErrorAttachments = GetErrorAttachments;
                Distribute.ReleaseAvailable = OnReleaseAvailable;

                // Event handlers
                Crashes.SendingErrorReport += SendingErrorReportHandler;
                Crashes.SentErrorReport += SentErrorReportHandler;
                Crashes.FailedToSendErrorReport += FailedToSendErrorReportHandler;
                Push.PushNotificationReceived += PrintNotification;

                AppCenterLog.Assert(LogTag, "AppCenter.Configured=" + AppCenter.Configured);
                AppCenter.SetLogUrl("https://in-integration.dev.avalanch.es");

                Distribute.SetInstallUrl("https://install.portal-server-core-integration.dev.avalanch.es");
                Distribute.SetApiUrl("https://api-gateway-core-integration.dev.avalanch.es/v0.1");
                Auth.SetConfigUrl("https://mbaasapi0fde96d9b.blob.core.windows.net");
                Data.SetTokenExchangeUrl("https://token-exchange-mbaas-integration.dev.avalanch.es/v0.1");

                var appSecrets = GetAppSecretDictionary();
                AppCenter.Start($"uwp={appSecrets[XamarinDevice.UWP]};android={appSecrets[XamarinDevice.Android]};ios={appSecrets[XamarinDevice.iOS]}", typeof(Analytics), typeof(Crashes), typeof(Distribute), typeof(Auth), typeof(Data));
                if (Current.Properties.ContainsKey(Constants.UserId) && Current.Properties[Constants.UserId] is string id)
                {
                    AppCenter.SetUserId(id);
                }

                // Work around for SetUserId race condition.
                AppCenter.Start(typeof(Push));
                AppCenter.IsEnabledAsync().ContinueWith(enabled =>
                {
                    AppCenterLog.Info(LogTag, "AppCenter.Enabled=" + enabled.Result);
                });
                AppCenter.GetInstallIdAsync().ContinueWith(installId =>
                {
                    AppCenterLog.Info(LogTag, "AppCenter.InstallId=" + installId.Result);
                });
                AppCenterLog.Info(LogTag, "AppCenter.SdkVersion=" + AppCenter.SdkVersion);
                Crashes.HasCrashedInLastSessionAsync().ContinueWith(hasCrashed =>
                {
                    AppCenterLog.Info(LogTag, "Crashes.HasCrashedInLastSession=" + hasCrashed.Result);
                });
                Crashes.GetLastSessionCrashReportAsync().ContinueWith(task =>
                {
                    AppCenterLog.Info(LogTag, "Crashes.LastSessionCrashReport.StackTrace=" + task.Result?.StackTrace);
                });
            }
        }

        static IReadOnlyDictionary<string, string> GetAppSecretDictionary()
        {
            // If user has selected another Auth Type, override the secret dictionary accordingly.
            var persistedAuthType = AuthTypeUtils.GetPersistedAuthType();
            switch (persistedAuthType)
            {
                case AuthType.AAD:
                    return AADAuthAppSecrets;
                case AuthType.B2C:
                default:
                    return B2CAuthAppSecrets;
            }
        }

        static void PrintNotification(object sender, PushNotificationReceivedEventArgs e)
        {
            Xamarin.Forms.Device.BeginInvokeOnMainThread(() =>
            {
                var message = e.Message;
                if (e.CustomData != null)
                {
                    message += "\nCustom data = {" + string.Join(",", e.CustomData.Select(kv => kv.Key + "=" + kv.Value)) + "}";
                }
                Current.MainPage.DisplayAlert(e.Title, message, "OK");
            });
        }

        static void SendingErrorReportHandler(object sender, SendingErrorReportEventArgs e)
        {
            AppCenterLog.Info(LogTag, "Sending error report");
        }

        static void SentErrorReportHandler(object sender, SentErrorReportEventArgs e)
        {
            AppCenterLog.Info(LogTag, "Sent error report");
        }

        static void FailedToSendErrorReportHandler(object sender, FailedToSendErrorReportEventArgs e)
        {
            AppCenterLog.Info(LogTag, "Failed to send error report");
        }

        bool ShouldProcess(ErrorReport report)
        {
            AppCenterLog.Info(LogTag, "Determining whether to process error report");
            return true;
        }

        bool ConfirmationHandler()
        {
            Xamarin.Forms.Device.BeginInvokeOnMainThread(() =>
            {
                Current.MainPage.DisplayActionSheet("Crash detected. Send anonymous crash report?", null, null, "Send", "Always Send", "Don't Send").ContinueWith((arg) =>
                {
                    var answer = arg.Result;
                    UserConfirmation userConfirmationSelection;
                    if (answer == "Send")
                    {
                        userConfirmationSelection = UserConfirmation.Send;
                    }
                    else if (answer == "Always Send")
                    {
                        userConfirmationSelection = UserConfirmation.AlwaysSend;
                    }
                    else
                    {
                        userConfirmationSelection = UserConfirmation.DontSend;
                    }
                    AppCenterLog.Debug(LogTag, "User selected confirmation option: \"" + answer + "\"");
                    Crashes.NotifyUserConfirmation(userConfirmationSelection);
                });
            });

            return true;
        }

        static IEnumerable<ErrorAttachmentLog> GetErrorAttachments(ErrorReport report)
        {
            var attachments = new List<ErrorAttachmentLog>();
            if (Current.Properties.TryGetValue(CrashesContentPage.TextAttachmentKey, out var textAttachment) &&
                textAttachment is string text)
            {
                var attachment = ErrorAttachmentLog.AttachmentWithText(text, "hello.txt");
                attachments.Add(attachment);
            }
            if (Current.Properties.TryGetValue(CrashesContentPage.FileAttachmentKey, out var fileAttachment) &&
                fileAttachment is string file)
            {
                var filePicker = DependencyService.Get<IFilePicker>();
                if (filePicker != null)
                {
                    try
                    {
                        var result = filePicker.ReadFile(file);
                        if (result != null)
                        {
                            var attachment = ErrorAttachmentLog.AttachmentWithBinary(result.Item1, result.Item2, result.Item3);
                            attachments.Add(attachment);
                        }
                    }
                    catch (Exception e)
                    {
                        AppCenterLog.Warn(LogTag, "Couldn't read file attachment", e);
                        Current.Properties.Remove(CrashesContentPage.FileAttachmentKey);
                    }
                }
            }
            return attachments;
        }

        bool OnReleaseAvailable(ReleaseDetails releaseDetails)
        {
            AppCenterLog.Info(LogTag, "OnReleaseAvailable id=" + releaseDetails.Id
                                            + " version=" + releaseDetails.Version
                                            + " releaseNotesUrl=" + releaseDetails.ReleaseNotesUrl);
            var custom = releaseDetails.ReleaseNotes?.ToLowerInvariant().Contains("custom") ?? false;
            if (custom)
            {
                var title = "Version " + releaseDetails.ShortVersion + " available!";
                Task answer;
                if (releaseDetails.MandatoryUpdate)
                {
                    answer = Current.MainPage.DisplayAlert(title, releaseDetails.ReleaseNotes, "Update now!");
                }
                else
                {
                    answer = Current.MainPage.DisplayAlert(title, releaseDetails.ReleaseNotes, "Update now!", "Maybe tomorrow...");
                }
                answer.ContinueWith((task) =>
                {
                    if (releaseDetails.MandatoryUpdate || ((Task<bool>)task).Result)
                    {
                        Distribute.NotifyUpdateAction(UpdateAction.Update);
                    }
                    else
                    {
                        Distribute.NotifyUpdateAction(UpdateAction.Postpone);
                    }
                });
            }
            return custom;
        }
    }
}
