using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace Akass.Optimizer;

internal sealed partial class MainWindow
{
    private GitHubUpdate? availableUpdate;
    private DateTime nextUpdateCheck;
    private bool checkingForUpdates;
    private bool checkedForUpdates;
    private bool updateCheckFailed;
    private Action? refreshUpdatePanel;
    internal string? PendingInstaller { get; private set; }

    private Border UpdatesPanel()
    {
        bool installed = GitHubUpdate.IsInstalled();
        var message = Muted("");
        var check = ActionButton(L("Check for updates", "التحقق من التحديثات", "Güncellemeleri denetle"),
            () => CheckForUpdates(manual: true));
        var install = ActionButton(L("Install update", "تثبيت التحديث", "Güncellemeyi yükle"), InstallUpdate, true);
        var releases = ActionButton(L("Open GitHub Releases", "فتح إصدارات GitHub", "GitHub sürümlerini aç"),
            () => OpenShell(GitHubUpdate.ReleasesPage));
        AutomationProperties.SetAutomationId(check, "CheckForUpdates");
        AutomationProperties.SetAutomationId(install, "InstallUpdate");
        refreshUpdatePanel = () =>
        {
            message.Text = UpdateMessage();
            check.IsEnabled = !checkingForUpdates;
            install.Visibility = availableUpdate != null && installed ? Visibility.Visible : Visibility.Collapsed;
        };
        refreshUpdatePanel();
        var actions = Actions(check, install, releases);
        return installed
            ? Section(L("Updates", "التحديثات", "Güncellemeler"), message, actions)
            : Section(L("Updates", "التحديثات", "Güncellemeler"), message, actions,
                Intro(L("For this portable copy, download and extract the latest ZIP from GitHub Releases.",
                    "لتحديث هذه النسخة المحمولة، نزّل أحدث ملف ZIP من إصدارات GitHub وفكّ ضغطه.",
                    "Bu taşınabilir kopyayı güncellemek için GitHub sürümlerinden son ZIP dosyasını indirip çıkarın.")));
    }

    private string UpdateMessage()
    {
        if (checkingForUpdates) return L("Checking GitHub for updates…", "جارٍ التحقق من تحديثات GitHub…", "GitHub güncellemeleri denetleniyor…");
        if (updateCheckFailed) return L("Could not check for updates. Try again or open GitHub Releases.",
            "تعذّر التحقق من التحديثات. أعد المحاولة أو افتح إصدارات GitHub.", "Güncellemeler denetlenemedi. Tekrar deneyin veya GitHub sürümlerini açın.");
        if (availableUpdate is { } update) return L($"Version {update.Version} is available.",
            $"الإصدار {update.Version} متوفر.", $"{update.Version} sürümü mevcut.");
        return checkedForUpdates
            ? L($"Version {VersionLabel} is up to date.", $"الإصدار {VersionLabel} هو الأحدث.", $"{VersionLabel} sürümü güncel.")
            : L($"Current version {VersionLabel}.", $"الإصدار الحالي {VersionLabel}.", $"Mevcut sürüm {VersionLabel}.");
    }

    private async Task CheckForUpdates(bool manual)
    {
        if (checkingForUpdates || exiting) return;
        checkingForUpdates = true;
        updateCheckFailed = false;
        nextUpdateCheck = DateTime.UtcNow.AddHours(24);
        refreshUpdatePanel?.Invoke();
        bool newVersion = false;
        try
        {
            var release = await GitHubUpdate.CheckAsync(Version.Parse(VersionLabel), CancellationToken.None);
            newVersion = release != null && release.Version != availableUpdate?.Version;
            availableUpdate = release;
            checkedForUpdates = true;
        }
        catch (Exception)
        {
            updateCheckFailed = true;
            nextUpdateCheck = DateTime.UtcNow.AddHours(1);
        }
        finally { checkingForUpdates = false; }

        if (exiting) return;
        refreshUpdatePanel?.Invoke();
        if (manual || newVersion && IsVisible && !model.Busy) Status(UpdateMessage());
        if (newVersion && !IsVisible && tray.Visible && model.Preferences.Notifications)
            tray.ShowBalloonTip(5000, "AKASS Optimizer", UpdateMessage(), Forms.ToolTipIcon.Info);
    }

    private async Task InstallUpdate()
    {
        if (availableUpdate is not { } update || !GitHubUpdate.IsInstalled()) return;
        if (!Confirm(L($"Install version {update.Version}? AKASS Optimizer will close and reopen after the update.",
            $"هل تريد تثبيت الإصدار {update.Version}؟ سيُغلق AKASS Optimizer ويُفتح بعد التحديث.",
            $"{update.Version} sürümü yüklensin mi? AKASS Optimizer kapanıp güncellemeden sonra yeniden açılacak."))) return;

        PendingInstaller = await Run(L("Downloading and verifying the GitHub installer…",
            "جارٍ تنزيل مثبّت GitHub والتحقق منه…", "GitHub yükleyicisi indiriliyor ve doğrulanıyor…"),
            token => update.DownloadAsync(model.Store.DirectoryPath, token));
        exiting = true;
        Close();
        if (!exiting) PendingInstaller = null;
    }
}
