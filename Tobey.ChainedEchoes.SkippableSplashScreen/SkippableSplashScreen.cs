using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Tobey.ChainedEchoes.SkippableSplashScreen;
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class SkippableSplashScreen : BaseUnityPlugin
{
    internal static SkippableSplashScreen Instance;
    internal static ManualLogSource Log => Instance.Logger;

    internal Harmony Harmony = new(PluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        // enforce singleton
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }
    }

    private ConfigEntry<bool> AutoSkip;
    private void Start()
    {
        AutoSkip = Config.Bind(
            section: "General",
            key: "Auto-skip splash scene",
            defaultValue: false,
            description: "When enabled, the splash screen will be automatically skipped as soon as possible without the need to press a button."
        );
    }

    private void OnEnable() => Harmony.PatchAll(typeof(SkippableSplashScreen));
    private void OnDisable() => Harmony.UnpatchSelf();

    private IEnumerator HandleSplashScreenSkip()
    {
        yield return PrepSplashScreen();

        if (!AutoSkip.Value && gameObject.GetComponent<PressAnyKey>() == null)
        {   // only display the press any key component if we are not auto-skipping
            gameObject.AddComponent<PressAnyKey>();
        }

        yield return WaitUntilSkipRequested();
        SkipSplashScreen();
    }

    /// <summary>
    /// Sets up event bindings and awaits the workaround for GameStart.Progressing
    /// </summary>
    /// <returns></returns>
    private IEnumerator PrepSplashScreen()
    {
        SceneManager.activeSceneChanged -= SceneManager_activeSceneChanged;
        SceneManager.activeSceneChanged += SceneManager_activeSceneChanged;

        yield return AnimationPlayedWorkaround();
    }

    /// <summary>
    /// Workaround for how the GameStart.Progressing coroutine manually sets animationPlayed = false in the middle of its execution.
    /// </summary>
    /// <returns></returns>
    private IEnumerator AnimationPlayedWorkaround()
    {
        GameStart.animationPlayed = true;
        yield return new WaitWhile(() => GameStart.animationPlayed);
    }

    /// <summary>
    /// A WaitUntil wrapper that waits until the player presses any key or has enabled auto-skip in the config.
    /// </summary>
    /// <returns></returns>
    private WaitUntil WaitUntilSkipRequested() => new WaitUntil(() => Application.isFocused && (Input.anyKeyDown || AutoSkip.Value));

    /// <summary>
    /// Fades out all canvas images in the scene.
    /// </summary>
    private void FadeOutCanvasImages()
    {
        var canvas = FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            canvas.GetComponentInChildren<Animation>().Stop();
            foreach (var image in canvas.GetComponentsInChildren<Image>())
            {
                image.CrossFadeAlpha(0, 0.1f, true);
            }
        }
    }

    /// <summary>
    /// Does what it says on the tin.
    /// </summary>
    private void SkipSplashScreen()
    {
        Logger.LogMessage("Skipping splash scene...");
        FadeOutCanvasImages();
        FindObjectOfType<SplashScreenAnimation>()?.AnimationFinished(); // this seems the most consistent way to do this without wasting resources
    }

    /// <summary>
    /// When the scene changes away from the splash screen, ensures that the PressAnyKey component
    /// is destroyed and that our coroutines are cancelled.
    /// </summary>
    /// <param name="_"></param>
    /// <param name="__"></param>
    private void SceneManager_activeSceneChanged(Scene _, Scene __)
    {
        SceneManager.activeSceneChanged -= SceneManager_activeSceneChanged;
        StopAllCoroutines();
        Destroy(gameObject?.GetComponent<PressAnyKey>());
    }

    /// <summary>
    /// Displays a label informing the user they can press any key to skip the splash screen.
    /// </summary>
    private class PressAnyKey : MonoBehaviour
    {
        private void OnGUI()
        {
            var centeredStyle = GUI.skin.GetStyle("Box");
            centeredStyle.alignment = TextAnchor.MiddleCenter;
            centeredStyle.fontSize = 30;
            centeredStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(0, Screen.height - 200, Screen.width, 200), "Press any key to skip", centeredStyle);
        }
    }

    [HarmonyPatch(typeof(GameStart), nameof(GameStart.Progressing))]
    [HarmonyPostfix, HarmonyWrapSafe]
    public static void GameStart_Progressing_Postfix() => Instance.StartCoroutine(Instance.HandleSplashScreenSkip());

    [HarmonyPatch(typeof(SplashScreenAnimation), nameof(SplashScreenAnimation.AnimationFinished))]
    [HarmonyPostfix, HarmonyWrapSafe]
    public static void AnimationFinishedPostfix() => Destroy(Instance?.GetComponent<PressAnyKey>());
}
