using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>How a weapon mode reads the trigger.</summary>
public enum WeaponTriggerStyle
{
    /// <summary>Keep firing volleys on a cadence for as long as the trigger is held. The machine gun.</summary>
    Automatic,
    /// <summary>Hand the press, hold and release to the emitters. The portal guns and the rail gun.</summary>
    Manual,
}

/// <summary>
/// One slot in the weapon's mode loop: the emitters it mounts, how it fires, and the rig it makes
/// visible while it is selected. Every mode owns its own visual root, so switching modes is a matter
/// of hiding one rig and showing another.
/// </summary>
[Serializable]
public sealed class WeaponModeConfig
{
    [Tooltip("Name shown in the Inspector and used for logging.")]
    public string name = "Mode";

    [Tooltip("Automatic fires volleys on a cadence; Manual gives the trigger to the emitters.")]
    public WeaponTriggerStyle style = WeaponTriggerStyle.Manual;

    [Tooltip("Shots per second, automatic modes only.")]
    public float fireRate = 5f;

    [Tooltip("The rig this mode shows. Leave empty to toggle the emitter objects themselves.")]
    public GameObject visual;

    [Tooltip("Emitters on the left mount. All of them fire together when the left mount is triggered.")]
    public WeaponEmitter[] left = Array.Empty<WeaponEmitter>();

    [Tooltip("Emitters on the right mount.")]
    public WeaponEmitter[] right = Array.Empty<WeaponEmitter>();
}

/// <summary>
/// Orchestrates the emitters mounted on a character: reads the fire input, paces automatic shots, and
/// switches between whole weapon modes.
///
/// A mode is a self-contained loadout - a visible rig, the emitters on its left and right mounts, and
/// the rule for reading the trigger. Automatic modes keep the old behaviour: the mounts alternate so
/// the two never fire on the same frame. Manual modes hand the press, hold and release straight to the
/// emitters, which is how a portal gun can hold a beam and a rail gun can charge.
///
/// The mouse wheel loops through the modes, and the project's Next/Previous actions (1 and 2) do the
/// same thing for keyboard testing. Switching cancels whatever the outgoing emitters were doing, so a
/// beam never survives a mode change and a charge never fires after its gun is gone.
/// </summary>
public class Weapon : MonoBehaviour
{
    [Header("Modes")]
    [Tooltip("The loop the mouse wheel walks through. At least one mode is required.")]
    [SerializeField] WeaponModeConfig[] modes = Array.Empty<WeaponModeConfig>();
    [Tooltip("Mode selected when play starts.")]
    [SerializeField] int startingMode;

    [Header("Mode switching")]
    [Tooltip("How much scroll has to accumulate before the mode changes. One mouse-wheel notch is " +
             "around 1 on a Mac and 120 on Windows, so 1 catches both without firing twice per notch.")]
    [SerializeField] float scrollStep = 1f;
    [Tooltip("Minimum time between two scroll-triggered mode changes, so a fast wheel does not skip modes.")]
    [SerializeField] float scrollCooldown = 0.18f;
    [Tooltip("Flip this if the wheel feels backwards for your platform.")]
    [SerializeField] bool invertScroll;
    [Tooltip("Brief pause after a mode change before the new mode can fire, so the trigger does not " +
             "fire the incoming gun on the same frame the old one was put away.")]
    [SerializeField] float modeSwitchCooldown = 0.15f;

    InputAction fireAction;
    InputAction nextAction;
    InputAction previousAction;

    float nextShotTime;
    float scrollAccumulator;
    float nextScrollTime;
    float scrollIdleTime;

    // Which mount is due next in an automatic mode. Kept across trigger pulls on purpose: tapping
    // repeatedly keeps alternating rather than firing the same mount every time.
    bool leftIsNext = true;

    bool leftHeld;
    bool rightHeld;

    int modeIndex = -1;

    /// <summary>The mode currently driving the weapon.</summary>
    public WeaponModeConfig CurrentMode => modeIndex >= 0 && modeIndex < modes.Length ? modes[modeIndex] : null;

    /// <summary>Index of the current mode in the loop.</summary>
    public int ModeIndex => modeIndex;

    /// <summary>How many modes the wheel can cycle through.</summary>
    public int ModeCount => modes != null ? modes.Length : 0;

    float Interval => CurrentMode != null ? 1f / Mathf.Max(CurrentMode.fireRate, 0.01f) : 0f;

    void Awake()
    {
        var actions = InputSystem.actions;
        if (actions != null)
        {
            fireAction = actions.FindAction("Attack");
            nextAction = actions.FindAction("Next");
            previousAction = actions.FindAction("Previous");
        }

        if (fireAction == null)
        {
            Debug.LogError($"{name}: no 'Attack' action in the project-wide input actions.", this);
        }

        if (modes == null || modes.Length == 0)
        {
            Debug.LogError($"{name}: Weapon has no modes, so it has nothing to fire.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < modes.Length; i++) CheckMode(modes[i], i);
        ApplyMode(Mathf.Clamp(startingMode, 0, modes.Length - 1));
    }

    /// <summary>Catches an emitter dropped into the wrong mount, which would read as inverted fire order.</summary>
    void CheckMode(WeaponModeConfig mode, int index)
    {
        if (mode == null)
        {
            Debug.LogError($"{name}: mode {index} is empty.", this);
            return;
        }

        CheckMount(mode.left, WeaponEmitter.Side.Left, $"{mode.name} left");
        CheckMount(mode.right, WeaponEmitter.Side.Right, $"{mode.name} right");

        if (!HasEmitters(mode.left) && !HasEmitters(mode.right))
        {
            Debug.LogWarning($"{name}: mode '{mode.name}' has no emitters assigned.", this);
        }
    }

    void CheckMount(WeaponEmitter[] mount, WeaponEmitter.Side expected, string slot)
    {
        if (mount == null) return;

        foreach (WeaponEmitter emitter in mount)
        {
            if (emitter == null || emitter.EmitterSide == expected) continue;

            Debug.LogWarning($"{name}: the {slot} mount holds a {emitter.EmitterSide} emitter " +
                             $"({emitter.name}). Move it to the other mount, or change that emitter's Side.", this);
        }
    }

    void Update()
    {
        ReadModeSwitching();

        WeaponModeConfig mode = CurrentMode;
        if (mode == null) return;

        if (mode.style == WeaponTriggerStyle.Automatic) UpdateAutomatic(mode);
        else UpdateManual(mode);
    }

    /// <summary>
    /// The wheel is read directly from the mouse: the project's action map has Next/Previous buttons
    /// but no scroll action, and adding one would only duplicate what the device already reports. The
    /// wheel accumulates so a trackpad's many small deltas read as one deliberate notch.
    /// </summary>
    void ReadModeSwitching()
    {
        if (nextAction != null && nextAction.WasPressedThisFrame()) CycleMode(1);
        else if (previousAction != null && previousAction.WasPressedThisFrame()) CycleMode(-1);

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            scrollAccumulator += scroll * (invertScroll ? -1f : 1f);
            scrollIdleTime = 0f;
        }
        else
        {
            scrollIdleTime += Time.unscaledDeltaTime;
            if (scrollIdleTime > 0.25f) scrollAccumulator = 0f;
        }

        if (Time.unscaledTime < nextScrollTime) return;
        if (Mathf.Abs(scrollAccumulator) < Mathf.Max(0.01f, scrollStep)) return;

        CycleMode(scrollAccumulator > 0f ? 1 : -1);
        scrollAccumulator = 0f;
        nextScrollTime = Time.unscaledTime + scrollCooldown;
    }

    /// <summary>Automatic modes: fire the due mount every interval while the trigger is held.</summary>
    void UpdateAutomatic(WeaponModeConfig mode)
    {
        if (fireAction == null || !fireAction.IsPressed()) return;
        if (Time.time < nextShotTime) return;

        nextShotTime = Time.time + Interval;
        FireAutomatic(mode);
    }

    /// <summary>
    /// Manual modes: the mouse buttons are separate triggers. Left drives the left mount, right drives
    /// the right mount, and each emitter decides whether that means a beam, a charge or something else.
    /// </summary>
    void UpdateManual(WeaponModeConfig mode)
    {
        bool left = fireAction != null && fireAction.IsPressed();
        bool right = Mouse.current != null && Mouse.current.rightButton.isPressed;

        if (left != leftHeld)
        {
            leftHeld = left;
            if (left) TriggerDown(mode.left);
            else TriggerUp(mode.left);
        }

        if (right != rightHeld)
        {
            rightHeld = right;
            if (right) TriggerDown(mode.right);
            else TriggerUp(mode.right);
        }

        float dt = Time.deltaTime;
        if (left) TriggerHeld(mode.left, dt);
        if (right) TriggerHeld(mode.right, dt);
    }

    /// <summary>Fires whichever mount is due in an automatic mode - every gun on it - and swaps next.</summary>
    void FireAutomatic(WeaponModeConfig mode)
    {
        WeaponEmitter[] due = leftIsNext ? mode.left : mode.right;

        // A mount with nothing fitted falls back to the other one, so a one-sided weapon keeps firing
        // every interval instead of skipping every other shot.
        if (!HasEmitters(due)) due = leftIsNext ? mode.right : mode.left;
        if (!HasEmitters(due)) return;

        foreach (WeaponEmitter emitter in due)
        {
            if (emitter != null) emitter.Fire(Interval);
        }

        leftIsNext = !leftIsNext;
    }

    /// <summary>
    /// Fires one volley now. Kept for code that drives the weapon directly; held modes treat it as the
    /// trigger going down on the left mount.
    /// </summary>
    public void Fire()
    {
        WeaponModeConfig mode = CurrentMode;
        if (mode == null) return;

        if (mode.style == WeaponTriggerStyle.Automatic) FireAutomatic(mode);
        else TriggerDown(mode.left);
    }

    /// <summary>Steps the mode loop. Positive goes forward, negative back; it wraps at both ends.</summary>
    public void CycleMode(int direction)
    {
        if (modes == null || modes.Length == 0 || direction == 0) return;
        SetMode(modeIndex + Math.Sign(direction));
    }

    /// <summary>
    /// Selects a mode by index, wrapping. The outgoing emitters are unequipped first - a beam or a
    /// charge does not survive the swap - then the old rig is hidden and the new one shown.
    /// </summary>
    public void SetMode(int index)
    {
        if (modes == null || modes.Length == 0) return;

        index = ((index % modes.Length) + modes.Length) % modes.Length;
        if (index == modeIndex) return;

        CancelTriggers();
        HideAllVisuals();

        modeIndex = index;
        WeaponModeConfig mode = modes[index];

        if (mode.visual != null) mode.visual.SetActive(true);
        else ShowEmitters(mode, true);

        leftHeld = false;
        rightHeld = false;
        nextShotTime = Mathf.Max(nextShotTime, Time.time + modeSwitchCooldown);
    }

    /// <summary>Applies a mode without wrapping or cancelling: the initial selection at Awake.</summary>
    void ApplyMode(int index)
    {
        modeIndex = index;
        HideAllVisuals();

        WeaponModeConfig mode = modes[index];
        if (mode == null) return;

        if (mode.visual != null) mode.visual.SetActive(true);
        else ShowEmitters(mode, true);
    }

    void HideAllVisuals()
    {
        foreach (WeaponModeConfig mode in modes)
        {
            if (mode == null) continue;

            if (mode.visual != null) mode.visual.SetActive(false);
            else ShowEmitters(mode, false);
        }
    }

    static void ShowEmitters(WeaponModeConfig mode, bool visible)
    {
        ShowEmitters(mode.left, visible);
        ShowEmitters(mode.right, visible);
    }

    static void ShowEmitters(WeaponEmitter[] mount, bool visible)
    {
        if (mount == null) return;

        foreach (WeaponEmitter emitter in mount)
        {
            if (emitter != null) emitter.gameObject.SetActive(visible);
        }
    }

    /// <summary>Tells the current emitters the trigger has been taken away, without committing a shot.</summary>
    void CancelTriggers()
    {
        WeaponModeConfig mode = CurrentMode;
        if (mode == null) return;

        CancelMount(mode.left);
        CancelMount(mode.right);
    }

    static void CancelMount(WeaponEmitter[] mount)
    {
        if (mount == null) return;

        foreach (WeaponEmitter emitter in mount)
        {
            if (emitter != null) emitter.OnUnequipped();
        }
    }

    static void TriggerDown(WeaponEmitter[] mount)
    {
        if (mount == null) return;

        foreach (WeaponEmitter emitter in mount)
        {
            if (emitter != null) emitter.OnTriggerDown();
        }
    }

    static void TriggerHeld(WeaponEmitter[] mount, float deltaTime)
    {
        if (mount == null) return;

        foreach (WeaponEmitter emitter in mount)
        {
            if (emitter != null) emitter.OnTriggerHeld(deltaTime);
        }
    }

    static void TriggerUp(WeaponEmitter[] mount)
    {
        if (mount == null) return;

        foreach (WeaponEmitter emitter in mount)
        {
            if (emitter != null) emitter.OnTriggerUp();
        }
    }

    static bool HasEmitters(WeaponEmitter[] mount)
    {
        if (mount == null) return false;

        foreach (WeaponEmitter emitter in mount)
        {
            if (emitter != null) return true;
        }

        return false;
    }

    void OnDisable()
    {
        CancelTriggers();
        leftHeld = false;
        rightHeld = false;
    }
}
