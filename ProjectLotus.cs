using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP;
using Lotus;
using Lotus.Addons;
using Lotus.API;
using Lotus.API.Reactive;
using Lotus.API.Reactive.HookEvents;
using Lotus.GameModes;
using Lotus.GUI.Menus;
using Lotus.GUI.Patches;
using Lotus.Managers;
using Lotus.Options;
using Lotus.Roles2;
using Lotus.Roles2.Definitions.JanitorRole;
using UnityEngine;
using VentLib;
using VentLib.Networking.Handshake;
using VentLib.Networking.RPC;
using VentLib.Options.Game;
using VentLib.Utilities.Optionals;
using VentLib.Version;
using VentLib.Version.Git;
using VentLib.Version.Updater;
using VentLib.Version.Updater.Github;
using Version = VentLib.Version.Version;

[assembly: AssemblyVersion(ProjectLotus.CompileVersion)]
namespace Lotus;

[BepInPlugin(PluginGuid, "Lotus", $"{MajorVersion}.{MinorVersion}.{PatchVersion}")]
[BepInDependency("com.tealeaf.VentLib")]
[BepInProcess("Among Us.exe")]
public class ProjectLotus : BasePlugin, IGitVersionEmitter
{
    private static readonly StandardLogger log = LoggerFactory.GetLogger<StandardLogger>(typeof(ProjectLotus));
    private const string Id = "com.discussions.LotusContinued";

    public const string PluginGuid = Id;
    public const string CompileVersion = $"{MajorVersion}.{MinorVersion}.{PatchVersion}.{BuildNumber}";

    public const string MajorVersion = "1";
    public const string MinorVersion = "1"; // Update with each release
    public const string PatchVersion = "0";
    public const string BuildNumber = "0805";

    public static string PluginVersion = typeof(ProjectLotus).Assembly.GetName().Version!.ToString();

    public readonly GitVersion CurrentVersion = new();

    public static readonly string ModName = "Project Lotus";
    public static readonly string ModColor = "#4FF918";

    public static DefinitionUnifier DefinitionUnifier = new();
    public static bool DevVersion = false;
    public static readonly string DevVersionStr = "Dev 28.05.2024";

    public Harmony Harmony { get; } = new(PluginGuid);
    public static string CredentialsText = null!;

    public static ModUpdater ModUpdater = null!;

    public static bool FinishedLoading;


    public ProjectLotus()
    {
#if DEBUG
        DevVersion = true;
        RpcMonitor.Enable();
#endif
        Instance = this;
        // Vents.Initialize();

        VersionControl versionControl = ModVersion.VersionControl = VersionControl.For(this);
        versionControl.AddVersionReceiver(ReceiveVersion);
        PluginDataManager.TemplateManager.RegisterTag("lobby-join", "Tag for the template shown to players joining the lobby.");

        ModUpdater = ModUpdater.Default();
        ModUpdater.EstablishConnection();
        ModUpdater.RegisterReleaseCallback(BeginUpdate, true);

#if !DEBUG
        Profilers.Global.SetActive(false);
#endif
    }

    private void BeginUpdate(Release release)
    {
        UnityOptional<ModUpdateMenu>.Of(SplashPatch.ModUpdateMenu).Handle(o => o.Open(), () => SplashPatch.UpdateReady = true);
        ModUpdateMenu.AddUpdateItem("Lotus", null, ex => ModUpdater.Update(errorCallback: ex)!);
        Assembly ventAssembly = typeof(Vents).Assembly;

        if (release.ContainsDLL($"{ventAssembly.GetName().Name!}.dll"))
            ModUpdateMenu.AddUpdateItem("VentFramework", null, ex => ModUpdater.Update(ventAssembly, ex)!);
    }

    public static NormalGameOptionsV07 NormalOptions => GameOptionsManager.Instance.currentNormalGameOptions;


    public static List<byte> ResetCamPlayerList = null!;

    public static GameModeManager GameModeManager;
    public static ProjectLotus Instance = null!;

    public override void Load()
    {
        //Profilers.Global.SetActive(false);
        log.Info($"{Application.version}", "AmongUs Version");

        GameOptionController.Enable();
        GameModeManager = new GameModeManager();

        log.Info(CurrentVersion.ToString(), "GitVersion");

        // Setup, order matters here

        /*StaticEditor.Register(Assembly.GetExecutingAssembly());*/
        Harmony.PatchAll(Assembly.GetExecutingAssembly());
        DefinitionUnifier.RegisterRoleComponents(typeof(ProjectLotus).Assembly);
        AddonManager.ImportAddons();

        DefinitionUnifier unifier = new();
        unifier.RegisterRoleComponents(this.GetType().Assembly);
        UnifiedRoleDefinition unifiedRoleDefinition = unifier.Unify(new JanitorNew());

        GameModeManager.Setup();
        ShowerPages.InitPages();

        FinishedLoading = true;
        log.High("Finished Initializing Project Lotus. Sending Post-Initialization Event");
        Hooks.ModHooks.LotusInitializedHook.Propagate(EmptyHookEvent.Hook);
    }

    public GitVersion Version() => CurrentVersion;

    public HandshakeResult HandshakeFilter(Version handshake)
    {
        if (handshake is NoVersion) return HandshakeResult.FailDoNothing;
        if (handshake is AmongUsMenuVersion) return HandshakeResult.FailDoNothing;
        if (handshake is not GitVersion git) return HandshakeResult.DisableRPC;
        if (git.MajorVersion != CurrentVersion.MajorVersion && git.MinorVersion != CurrentVersion.MinorVersion) return HandshakeResult.FailDoNothing;
        return HandshakeResult.PassDoNothing;
    }

    private static void ReceiveVersion(Version version, PlayerControl player)
    {
        if (player == null) return;
        if (version is AmongUsMenuVersion)
        {
            PluginDataManager.BanManager.BanWithReason(player, "Cheating - Among Us Menu Auto Ban");
            return;
        }
        if (version is not NoVersion)
        {
            //ModRPC rpc = Vents.FindRPC((uint)ModCalls.SendOptionPreview)!;
            //rpc.Send(new[] { player.GetClientId() }, new BatchList<Option>(OptionManager.GetManager().GetOptions()));
        }

        PluginDataManager.TemplateManager.GetTemplates("lobby-join")?.ForEach(t =>
        {
            if (player == null) return;
            t.SendMessage(PlayerControl.LocalPlayer, player);
        });

        Hooks.NetworkHooks.ReceiveVersionHook.Propagate(new ReceiveVersionHookEvent(player, version));
    }
}