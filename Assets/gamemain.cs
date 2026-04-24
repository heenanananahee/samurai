using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
#endif

public class gamemain : MonoBehaviour
{
    [Header("Required")]
    [SerializeField] private Button attackButton;
    [SerializeField] private Animator samuraiAnimator;
    [SerializeField] private GameObject fxSamuraiSlashPrefab;
    [SerializeField] private GameObject idleObject;
    [SerializeField] private GameObject runObject;
    [SerializeField] private GameObject attackObject;
    [SerializeField] private Transform characterRoot;

    [Header("Animation States")]
    [SerializeField] private string runStateName = "RUN_0";
    [SerializeField] private string idleStateName = "samurai_idel";
    [SerializeField] private string attackStateName = "ATTACK";
    [SerializeField] private string attackTriggerName = "";
    [SerializeField] private string attackBoolName = "";
    [SerializeField] private float attackDuration = 0.9f;
    [SerializeField] private bool useManualAttackSpriteFallback = true;
    [SerializeField] private bool strictSetupMode = true;
    [SerializeField] private bool enableKeyboardFallback = false;

    [Header("FX Spawn")]
    [SerializeField] private Transform fxSpawnPoint;
    [SerializeField] private Vector3 fxSpawnOffset = Vector3.zero;
    [SerializeField] private bool enableAttackFx = true;
    [SerializeField] private bool forceDisableSceneFxUntilAttack = true;
    [SerializeField] private bool disableAllFxForNow = true;

    [Header("UI")]
    [SerializeField] private string idleText = "IDLE";
    [SerializeField] private string attackText = "ATTACK";

    [Header("Entrance Move")]
    [SerializeField] private bool useEntranceMove = true;
    [SerializeField] private float entranceStartX = -9.4f;
    [SerializeField] private float entranceTargetX = 0f;
    [SerializeField] private float entranceDuration = 5f;
    [SerializeField] private bool disableOtherAnimatorsOnStart = false;

    private TMP_Text _tmpButtonText;
    private Text _legacyButtonText;
    private bool _isAttacking;
    private Coroutine _attackRoutine;
    private Coroutine _entranceRoutine;
    private Coroutine _manualAttackSpriteRoutine;
    private string _logFilePath;
    private string _projectLogFilePath;
    private bool _isEntranceDone;
    private string _resolvedIdleStateName;
    private string _resolvedRunStateName;
    private string _resolvedAttackStateName;
    private Animator _runAnimator;
    private Animator _attackAnimator;
    private Animator _idleAnimator;
    private Vector3 _characterWorldPosition;
    private SpriteRenderer _attackSpriteRenderer;
    private Sprite[] _manualAttackFrames;

    // 1) Attack 전환 알림용 델리게이트
    public delegate void AttackTransitionDelegate(Vector3 spawnPosition);
    // 2) Main이 구독해서 FX 요청으로 연결
    public event AttackTransitionDelegate OnAttackTransitioned;

    private void OnEnable()
    {
        OnAttackTransitioned += HandleAttackTransitioned;
    }

    private void OnDisable()
    {
        OnAttackTransitioned -= HandleAttackTransitioned;
    }

    private void Start()
    {
        _logFilePath = Path.Combine(Application.persistentDataPath, "samurai_attack_log.txt");
        _projectLogFilePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "samurai_attack_log_runtime.txt"));
        WriteLog("========== New Play Session ==========");
        WriteLog("gamemain Start");

        if (disableAllFxForNow)
        {
            enableAttackFx = false;
            forceDisableSceneFxUntilAttack = true;
            WriteLog("FX disabled by setting: disableAllFxForNow=true");
        }
        if (strictSetupMode)
        {
            useManualAttackSpriteFallback = false;
            WriteLog("Strict setup mode ON: runtime attack fallbacks disabled.");
        }

        ValidateReferences();
        AutoAssignStateObjectsIfMissing();
        ResolveAnimatorStates();
        HideSceneFxSourceIfNeeded();
        CacheStateAnimators();
        CacheManualAttackFrames();
        DumpRuntimeBindings("AfterCacheStateAnimators");
        EnsureEventSystemExists();
        CacheButtonText();
        SetButtonLabel(idleText);
        InitializeCharacterPosition();
        ForceIdle();

        if (attackButton != null)
        {
            attackButton.onClick.AddListener(HandleAttackButtonClicked);
            attackButton.interactable = !useEntranceMove;
            WriteLog("Attack button connected.");
        }

        if (useEntranceMove)
        {
            _entranceRoutine = StartCoroutine(EntranceRoutine());
        }
        else
        {
            _isEntranceDone = true;
        }
    }

    private void Update()
    {
        if (!_isEntranceDone || _isAttacking)
        {
            return;
        }

        // Keyboard fallback for environments where UI click can be lost.
        if (enableKeyboardFallback && Input.GetKeyDown(KeyCode.Space))
        {
            WriteLog("Input fallback: Space pressed.");
            StartAttack();
            return;
        }

        // Mouse fallback: if Button.onClick does not fire, detect click on button rect directly.
        if (attackButton != null && Input.GetMouseButtonDown(0))
        {
            RectTransform rt = attackButton.GetComponent<RectTransform>();
            if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null))
            {
                WriteLog("Input fallback: mouse click hit attack button rect.");
                HandleAttackButtonClicked();
            }
        }
    }

    private void HandleAttackButtonClicked()
    {
        WriteLog("UI click received: attack button.");

        if (!_isEntranceDone)
        {
            WriteLog("CHECK2_FAIL: Button pressed before entrance complete.");
            return;
        }

        if (_isAttacking)
        {
            WriteLog("Attack ignored: already in progress");
            return;
        }

        StartAttack();
    }

    private void StartAttack()
    {
        if (_isAttacking)
        {
            return;
        }

        _isAttacking = true;
        SetButtonLabel(attackText);
        SetVisualStateForAttack();
        ApplyCharacterPositionToStateObjects();
        DumpRuntimeBindings("BeforePlayAttack");
        PlayAttackAnimation();

        // Attack으로 전환되었음을 Main 이벤트(델리게이트)로 알림
        Vector3 spawnPosition = ResolveFxSpawnPosition();
        OnAttackTransitioned?.Invoke(spawnPosition);
        WriteLog("CHECK3_OK: Attack animation started and delegate invoked.");

        if (_attackRoutine != null)
        {
            StopCoroutine(_attackRoutine);
        }
        _attackRoutine = StartCoroutine(AttackRoutine());
    }

    private System.Collections.IEnumerator AttackRoutine()
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, attackDuration));

        if (_manualAttackSpriteRoutine != null)
        {
            StopCoroutine(_manualAttackSpriteRoutine);
            _manualAttackSpriteRoutine = null;
        }

        ForceIdle();
        SetButtonLabel(idleText);
        _isAttacking = false;
        WriteLog("CHECK3_OK: Returned to idle after attack.");
    }

    private void PlayAttackAnimation()
    {
        Animator targetAnimator = _attackAnimator != null ? _attackAnimator : samuraiAnimator;
        if (targetAnimator == null)
        {
            WriteLog("ERROR: samuraiAnimator is null");
            return;
        }

        // Ensure the object is active and restart from a clean frame.
        if (!targetAnimator.gameObject.activeSelf)
        {
            targetAnimator.gameObject.SetActive(true);
        }
        targetAnimator.enabled = true;
        targetAnimator.speed = 1f;
        targetAnimator.Rebind();
        targetAnimator.Update(0f);

        // Resolve attack state against the actual attack animator, not idle animator.
        string runtimeAttackState = ResolveStateOnAnimator(
            targetAnimator,
            attackStateName,
            new[] { "ATTACK", "Attack_Auto", "New Animationattack", "Attack", "attack" }
        );

        bool playedAttack = TryPlayStateOnAnimator(targetAnimator, runtimeAttackState);
        if (strictSetupMode)
        {
            if (!playedAttack)
            {
                WriteLog("CHECK3_FAIL: Attack state not found in strict mode. preferred=" + attackStateName + ", resolved=" + runtimeAttackState);
            }
            else
            {
                WriteLog("CHECK3_OK: Attack state played in strict mode -> " + runtimeAttackState);
            }
            return;
        }

        if (!playedAttack)
        {
            playedAttack = TryPlayByClipKeyword(targetAnimator, "attack");
        }
        if (!playedAttack)
        {
            targetAnimator.Play(0, 0, 0f);
            targetAnimator.Update(0f);
            playedAttack = true;
            WriteLog("CHECK3_OK: Attack forced by layer-0 default state.");
        }

        if (!string.IsNullOrEmpty(attackBoolName))
        {
            if (HasAnimatorParameter(targetAnimator, attackBoolName, AnimatorControllerParameterType.Bool))
            {
                targetAnimator.SetBool(attackBoolName, true);
            }
        }

        if (!string.IsNullOrEmpty(attackTriggerName))
        {
            if (HasAnimatorParameter(targetAnimator, attackTriggerName, AnimatorControllerParameterType.Trigger))
            {
                targetAnimator.SetTrigger(attackTriggerName);
            }
        }

        if (useManualAttackSpriteFallback && _attackSpriteRenderer != null && _manualAttackFrames != null && _manualAttackFrames.Length > 1)
        {
            if (_manualAttackSpriteRoutine != null)
            {
                StopCoroutine(_manualAttackSpriteRoutine);
            }
            _manualAttackSpriteRoutine = StartCoroutine(PlayManualAttackSprites());
            WriteLog("CHECK3_FIX: Manual attack sprite fallback started. frames=" + _manualAttackFrames.Length);
        }

        WriteLog(playedAttack ? "CHECK3_OK: Attack state play requested." : "CHECK3_WARN: Attack state not directly found. Trigger/bool fallback used.");
    }

    private void ForceIdle()
    {
        SetVisualStateForIdle();
        ApplyCharacterPositionToStateObjects();
        ApplySceneFxActive(false);

        Animator idleAnimator = _idleAnimator != null ? _idleAnimator : samuraiAnimator;
        if (idleAnimator == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(attackBoolName))
        {
            if (HasAnimatorParameter(idleAnimator, attackBoolName, AnimatorControllerParameterType.Bool))
            {
                idleAnimator.SetBool(attackBoolName, false);
            }
        }

        bool playedIdle = TryPlayStateOnAnimator(idleAnimator, _resolvedIdleStateName);
        if (!playedIdle)
        {
            TryPlayByClipKeyword(idleAnimator, "idle");
            TryPlayByClipKeyword(idleAnimator, "idel");
        }
    }

    private System.Collections.IEnumerator EntranceRoutine()
    {
        if (samuraiAnimator == null)
        {
            WriteLog("CHECK1_FAIL: samuraiAnimator missing.");
            yield break;
        }

        Transform root = characterRoot != null ? characterRoot : samuraiAnimator.transform;
        Vector3 start = _characterWorldPosition;
        start.x = entranceStartX;
        root.position = start;
        _characterWorldPosition = start;
        ApplyCharacterPositionToStateObjects();

        Vector3 target = root.position;
        target.x = entranceTargetX;

        WriteLog("CHECK1_START: Entrance begin from x=" + start.x.ToString("F3") + " to x=" + target.x.ToString("F3"));

        SetVisualStateForRun();
        bool playedRun = false;
        if (_runAnimator != null)
        {
            playedRun = TryPlayStateOnAnimator(_runAnimator, _resolvedRunStateName);
            if (!playedRun)
            {
                playedRun = TryPlayByClipKeyword(_runAnimator, "run") || TryPlayByClipKeyword(_runAnimator, "walk");
            }
        }
        else
        {
            playedRun = TryPlayStateOnAnimator(samuraiAnimator, _resolvedRunStateName);
            if (!playedRun)
            {
                playedRun = TryPlayByClipKeyword(samuraiAnimator, "run") || TryPlayByClipKeyword(samuraiAnimator, "walk");
            }
        }
        if (!playedRun)
        {
            playedRun = TryPlayByClipKeyword("run") || TryPlayByClipKeyword("walk");
        }
        if (!playedRun)
        {
            WriteLog("CHECK1_WARN: Run state not found -> " + runStateName);
        }

        float elapsed = 0f;
        float duration = Mathf.Max(0.1f, entranceDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _characterWorldPosition = Vector3.Lerp(start, target, t);
            root.position = _characterWorldPosition;
            ApplyCharacterPositionToStateObjects();
            yield return null;
        }

        _characterWorldPosition = target;
        root.position = _characterWorldPosition;
        ApplyCharacterPositionToStateObjects();
        ForceIdle();
        _isEntranceDone = true;
        if (attackButton != null)
        {
            attackButton.interactable = true;
        }

        WriteLog("CHECK1_OK: Entrance completed at x=" + _characterWorldPosition.x.ToString("F3"));
        WriteLog("CHECK2_OK: Waiting for button at center.");
    }

    private Vector3 ResolveFxSpawnPosition()
    {
        if (fxSpawnPoint != null)
        {
            return fxSpawnPoint.position + fxSpawnOffset;
        }

        if (samuraiAnimator != null)
        {
            return samuraiAnimator.transform.position + fxSpawnOffset;
        }

        return transform.position + fxSpawnOffset;
    }

    // 3) Main이 FxManager 싱글톤에 FX 생성 요청
    private void HandleAttackTransitioned(Vector3 spawnPosition)
    {
        if (disableAllFxForNow || !enableAttackFx)
        {
            WriteLog("FX skipped (disabled).");
            return;
        }

        FxManager.Instance.RequestSamuraiSlashFx(spawnPosition, fxSamuraiSlashPrefab);
        WriteLog("Main -> FxManager requested fx_samurai_slash at " + spawnPosition);
    }

    private void CacheButtonText()
    {
        if (attackButton == null)
        {
            return;
        }

        _tmpButtonText = attackButton.GetComponentInChildren<TMP_Text>(true);
        _legacyButtonText = attackButton.GetComponentInChildren<Text>(true);
    }

    private void SetButtonLabel(string value)
    {
        if (_tmpButtonText != null)
        {
            _tmpButtonText.text = value;
            return;
        }

        if (_legacyButtonText != null)
        {
            _legacyButtonText.text = value;
        }
    }

    private void ValidateReferences()
    {
        if (attackButton == null)
        {
            WriteLog("ERROR: attackButton is missing");
        }
        if (samuraiAnimator == null)
        {
            WriteLog("ERROR: samuraiAnimator is missing");
        }
        if (enableAttackFx && fxSamuraiSlashPrefab == null)
        {
            WriteLog("WARN: fxSamuraiSlashPrefab is missing");
        }
    }

    private void ResolveAnimatorStates()
    {
        _resolvedIdleStateName = ResolveFirstValidState(idleStateName, new[] { "samurai_idel", "IDLE_0", "Idle", "idle" });
        _resolvedRunStateName = ResolveFirstValidState(runStateName, new[] { "RUN_0", "Run", "run", "Walk", "walk" });
        _resolvedAttackStateName = ResolveFirstValidState(attackStateName, new[] { "ATTACK", "Attack_Auto", "New Animationattack", "Attack", "attack" });

        WriteLog("Resolved states -> idle: " + _resolvedIdleStateName + ", run: " + _resolvedRunStateName + ", attack: " + _resolvedAttackStateName);
        LogAnimatorClipNames(samuraiAnimator, "samuraiAnimator");
    }

    private string ResolveFirstValidState(string preferred, string[] fallbacks)
    {
        return ResolveStateOnAnimator(samuraiAnimator, preferred, fallbacks);
    }

    private string ResolveStateOnAnimator(Animator targetAnimator, string preferred, string[] fallbacks)
    {
        if (targetAnimator == null)
        {
            return preferred;
        }

        if (!string.IsNullOrEmpty(preferred) && targetAnimator.HasState(0, Animator.StringToHash(preferred)))
        {
            return preferred;
        }

        for (int i = 0; i < fallbacks.Length; i++)
        {
            string candidate = fallbacks[i];
            if (!string.IsNullOrEmpty(candidate) && targetAnimator.HasState(0, Animator.StringToHash(candidate)))
            {
                return candidate;
            }
        }

        return preferred;
    }

    private void DisableUnexpectedAnimators()
    {
        if (!disableOtherAnimatorsOnStart || samuraiAnimator == null)
        {
            return;
        }

        Animator[] allAnimators = FindObjectsOfType<Animator>(true);
        for (int i = 0; i < allAnimators.Length; i++)
        {
            Animator other = allAnimators[i];
            if (other == null || other == samuraiAnimator)
            {
                continue;
            }

            if (other.GetComponentInParent<Canvas>() != null)
            {
                continue;
            }

            other.gameObject.SetActive(false);
            WriteLog("CHECK0_FIX: Disabled extra animator object -> " + other.name);
        }
    }

    private void HideSceneFxSourceIfNeeded()
    {
        if (fxSamuraiSlashPrefab == null)
        {
            ForceDisableSceneEffectObjects();
            return;
        }

        if (fxSamuraiSlashPrefab.scene.IsValid() && fxSamuraiSlashPrefab.activeSelf)
        {
            fxSamuraiSlashPrefab.SetActive(false);
            WriteLog("CHECK0_FIX: Scene FX source hidden on start -> " + fxSamuraiSlashPrefab.name);
        }

        ForceDisableSceneEffectObjects();
    }

    private void EnsureEventSystemExists()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
        WriteLog("CHECK0_FIX: EventSystem auto-created.");
    }

    private bool TryPlayResolvedState(string stateName)
    {
        return TryPlayStateOnAnimator(samuraiAnimator, stateName);
    }

    private bool TryPlayStateOnAnimator(Animator targetAnimator, string stateName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(stateName))
        {
            return false;
        }

        int hash = Animator.StringToHash(stateName);
        if (!targetAnimator.HasState(0, hash))
        {
            return false;
        }

        targetAnimator.Play(stateName, 0, 0f);
        targetAnimator.Update(0f);
        return true;
    }

    private bool TryPlayByClipKeyword(string keyword)
    {
        return TryPlayByClipKeyword(samuraiAnimator, keyword);
    }

    private bool TryPlayByClipKeyword(Animator targetAnimator, string keyword)
    {
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null || string.IsNullOrEmpty(keyword))
        {
            return false;
        }

        AnimationClip[] clips = targetAnimator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null || string.IsNullOrEmpty(clip.name))
            {
                continue;
            }

            if (clip.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (TryPlayStateOnAnimator(targetAnimator, clip.name))
            {
                WriteLog("State fallback by clip keyword '" + keyword + "' -> " + clip.name);
                return true;
            }
        }

        return false;
    }

    private void LogAnimatorClipNames(Animator targetAnimator, string label)
    {
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null)
        {
            return;
        }

        AnimationClip[] clips = targetAnimator.runtimeAnimatorController.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null)
            {
                WriteLog(label + " clip[" + i + "] = " + clip.name);
            }
        }
    }

    private void CacheStateAnimators()
    {
        if (!strictSetupMode && idleObject == null)
        {
            idleObject = FindObjectByAnimatorClipKeyword("idle", "idel");
        }
        if (!strictSetupMode && runObject == null)
        {
            runObject = FindObjectByAnimatorClipKeyword("run", "walk");
        }
        if (!strictSetupMode && attackObject == null)
        {
            attackObject = FindObjectByAnimatorClipKeyword("attack");
        }

        _idleAnimator = idleObject != null ? idleObject.GetComponent<Animator>() : samuraiAnimator;
        _runAnimator = runObject != null ? runObject.GetComponent<Animator>() : null;
        _attackAnimator = attackObject != null ? attackObject.GetComponent<Animator>() : null;
        _attackSpriteRenderer = attackObject != null ? attackObject.GetComponent<SpriteRenderer>() : null;
        if (!strictSetupMode)
        {
            EnsureGeneratedAttackAnimationInEditor();
            ForceAssignAttackControllerInEditor();
        }
        if (_attackAnimator != null)
        {
            _attackAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        LogAnimatorClipNames(_idleAnimator, "idleAnimator");
        LogAnimatorClipNames(_runAnimator, "runAnimator");
        LogAnimatorClipNames(_attackAnimator, "attackAnimator");
    }

    private void CacheManualAttackFrames()
    {
        _manualAttackFrames = null;
        if (strictSetupMode)
        {
            return;
        }

#if UNITY_EDITOR
        _manualAttackFrames = FindAttackSpritesFromProject();
        WriteLog("CHECK3_INFO: Manual attack frames cached = " + (_manualAttackFrames != null ? _manualAttackFrames.Length : 0));
#endif
    }

    private System.Collections.IEnumerator PlayManualAttackSprites()
    {
        if (_attackSpriteRenderer == null || _manualAttackFrames == null || _manualAttackFrames.Length == 0)
        {
            yield break;
        }

        float total = Mathf.Max(0.1f, attackDuration);
        float step = total / Mathf.Max(1, _manualAttackFrames.Length);
        for (int i = 0; i < _manualAttackFrames.Length; i++)
        {
            _attackSpriteRenderer.sprite = _manualAttackFrames[i];
            yield return new WaitForSeconds(step);
        }

        _manualAttackSpriteRoutine = null;
    }

    private void ForceAssignAttackControllerInEditor()
    {
#if UNITY_EDITOR
        if (_attackAnimator == null)
        {
            return;
        }

        RuntimeAnimatorController forcedController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Generated/Attack_Auto.controller");
        if (forcedController == null)
        {
            forcedController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/ATTACK 1_0 (1).controller");
        }

        if (forcedController == null)
        {
            WriteLog("CHECK3_WARN: Forced attack controller not found.");
            return;
        }

        if (_attackAnimator.runtimeAnimatorController != forcedController)
        {
            _attackAnimator.runtimeAnimatorController = forcedController;
            WriteLog("CHECK3_FIX: Forced attack controller assigned -> ATTACK 1_0 (1).controller");
        }
#endif
    }

    private void EnsureGeneratedAttackAnimationInEditor()
    {
#if UNITY_EDITOR
        if (_attackAnimator == null)
        {
            return;
        }

        const string folder = "Assets/Generated";
        const string clipPath = "Assets/Generated/Attack_Auto.anim";
        const string controllerPath = "Assets/Generated/Attack_Auto.controller";

        if (!AssetDatabase.IsValidFolder(folder))
        {
            AssetDatabase.CreateFolder("Assets", "Generated");
        }

        List<Sprite> frames = new List<Sprite>(FindAttackSpritesFromProject());

        if (frames.Count < 2)
        {
            WriteLog("CHECK3_WARN: Cannot generate attack clip. Need sprites named like ATTACK 1_*. Found=" + frames.Count);
            return;
        }

        frames.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            clip.name = "Attack_Auto";
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        EditorCurveBinding spriteBinding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = string.Empty,
            propertyName = "m_Sprite"
        };

        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[frames.Count];
        float fps = 12f;
        for (int i = 0; i < frames.Count; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i / fps,
                value = frames[i]
            };
        }
        AnimationUtility.SetObjectReferenceCurve(clip, spriteBinding, keys);

        AnimationClipSettings clipSettings = AnimationUtility.GetAnimationClipSettings(clip);
        clipSettings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, clipSettings);
        EditorUtility.SetDirty(clip);

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        }

        AnimatorControllerLayer layer = controller.layers[0];
        AnimatorStateMachine sm = layer.stateMachine;
        AnimatorState state = null;
        ChildAnimatorState[] states = sm.states;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].state != null && states[i].state.name == "Attack_Auto")
            {
                state = states[i].state;
                break;
            }
        }
        if (state == null)
        {
            state = sm.AddState("Attack_Auto");
        }
        state.motion = clip;
        sm.defaultState = state;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        WriteLog("CHECK3_FIX: Generated attack clip/controller from ATTACK 1.png frames=" + frames.Count);
#endif
    }

    private Sprite[] FindAttackSpritesFromProject()
    {
#if UNITY_EDITOR
        List<Sprite> sprites = new List<Sprite>();
        string[] spriteGuids = AssetDatabase.FindAssets("t:Sprite");
        for (int i = 0; i < spriteGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(spriteGuids[i]);
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            for (int j = 0; j < assets.Length; j++)
            {
                Sprite sprite = assets[j] as Sprite;
                if (sprite == null || string.IsNullOrEmpty(sprite.name))
                {
                    continue;
                }

                if (sprite.name.IndexOf("ATTACK 1_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sprite.name.IndexOf("ATTACK_1_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sprites.Add(sprite);
                }
            }
        }

        sprites.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return sprites.ToArray();
#else
        return null;
#endif
    }

    private void AutoAssignStateObjectsIfMissing()
    {
        if (idleObject == null)
        {
            GameObject found = FindSceneObjectByNames("IDLE_0", "IDLE", "idle");
            if (found != null)
            {
                idleObject = found;
                WriteLog("Auto-assigned idleObject: " + idleObject.name);
            }
        }

        if (runObject == null)
        {
            GameObject found = FindSceneObjectByNames("RUN_0", "RUN", "run");
            if (found != null)
            {
                runObject = found;
                WriteLog("Auto-assigned runObject: " + runObject.name);
            }
        }

        if (attackObject == null)
        {
            GameObject found = FindSceneObjectByNames("ATTACK 1_0", "ATTACK_1_0", "ATTACK", "attack");
            if (found != null)
            {
                attackObject = found;
                WriteLog("Auto-assigned attackObject: " + attackObject.name);
            }
        }

        if (samuraiAnimator == null)
        {
            Animator fallbackAnimator = null;
            if (idleObject != null)
            {
                fallbackAnimator = idleObject.GetComponent<Animator>();
            }
            if (fallbackAnimator == null && runObject != null)
            {
                fallbackAnimator = runObject.GetComponent<Animator>();
            }
            if (fallbackAnimator == null && attackObject != null)
            {
                fallbackAnimator = attackObject.GetComponent<Animator>();
            }

            if (fallbackAnimator != null)
            {
                samuraiAnimator = fallbackAnimator;
                WriteLog("Auto-assigned samuraiAnimator: " + samuraiAnimator.gameObject.name);
            }
        }

        if (characterRoot == null)
        {
            if (idleObject != null)
            {
                characterRoot = idleObject.transform;
            }
            else if (samuraiAnimator != null)
            {
                characterRoot = samuraiAnimator.transform;
            }
        }

        if (!strictSetupMode)
        {
            CreateMissingStateObjectsIfNeeded();
        }
    }

    private void CreateMissingStateObjectsIfNeeded()
    {
        if (idleObject == null && runObject != null)
        {
            idleObject = CreateStateClone(runObject, "IDLE_0");
            WriteLog("CHECK_FIX: Recreated missing idleObject from runObject.");
        }
        else if (idleObject == null && attackObject != null)
        {
            idleObject = CreateStateClone(attackObject, "IDLE_0");
            WriteLog("CHECK_FIX: Recreated missing idleObject from attackObject.");
        }

        if (runObject == null && idleObject != null)
        {
            runObject = CreateStateClone(idleObject, "RUN_0");
            ApplyControllerIfFound(runObject, "Assets/RUN_0.controller");
            WriteLog("CHECK_FIX: Recreated missing runObject from idleObject.");
        }

        if (attackObject == null && idleObject != null)
        {
            attackObject = CreateStateClone(idleObject, "ATTACK 1_0");
            ApplyControllerIfFound(attackObject, "Assets/Generated/Attack_Auto.controller");
            ApplyControllerIfFound(attackObject, "Assets/ATTACK 1_0.controller");
            WriteLog("CHECK_FIX: Recreated missing attackObject from idleObject.");
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            return;
        }

        EditorApplication.delayCall += RebuildMissingStateObjectsInEditor;
    }

    [ContextMenu("Rebuild Missing State Objects (Editor)")]
    private void RebuildMissingStateObjectsInEditor()
    {
        if (this == null || Application.isPlaying)
        {
            return;
        }

        int before = CountAssignedStateObjects();
        AutoAssignStateObjectsIfMissing();
        int after = CountAssignedStateObjects();

        EditorUtility.SetDirty(this);
        if (gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        if (after > before)
        {
            WriteLog("CHECK_FIX: Missing state objects rebuilt in Editor and scene marked dirty.");
        }
    }
#endif

    private int CountAssignedStateObjects()
    {
        int count = 0;
        if (idleObject != null) count++;
        if (runObject != null) count++;
        if (attackObject != null) count++;
        return count;
    }

    private GameObject CreateStateClone(GameObject source, string newName)
    {
        if (source == null)
        {
            return null;
        }

        Transform parent = source.transform.parent;
        GameObject clone = Instantiate(source, source.transform.position, source.transform.rotation, parent);
        clone.name = newName;
        clone.transform.localScale = source.transform.localScale;
        clone.SetActive(false);
        return clone;
    }

    private void ApplyControllerIfFound(GameObject target, string assetPath)
    {
#if UNITY_EDITOR
        if (target == null || string.IsNullOrEmpty(assetPath))
        {
            return;
        }

        Animator animator = target.GetComponent<Animator>();
        if (animator == null)
        {
            return;
        }

        RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(assetPath);
        if (controller != null)
        {
            animator.runtimeAnimatorController = controller;
        }
#endif
    }

    private GameObject FindSceneObjectByNames(params string[] candidates)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null || !go.scene.IsValid() || go.scene != activeScene)
            {
                continue;
            }

            for (int c = 0; c < candidates.Length; c++)
            {
                string name = candidates[c];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (go.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return go;
                }
            }
        }

        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null || !go.scene.IsValid() || go.scene != activeScene)
            {
                continue;
            }

            for (int c = 0; c < candidates.Length; c++)
            {
                string name = candidates[c];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return go;
                }
            }
        }

        return null;
    }

    private void ForceDisableSceneEffectObjects()
    {
        if (!disableAllFxForNow)
        {
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go == null || !go.scene.IsValid() || go.scene != activeScene)
            {
                continue;
            }

            if (go.name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) < 0 &&
                go.name.IndexOf("fx", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (go.activeSelf)
            {
                go.SetActive(false);
                WriteLog("CHECK0_FIX: Disabled scene fx object -> " + go.name);
            }
        }
    }

    private GameObject FindObjectByAnimatorClipKeyword(params string[] keywords)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        Animator[] animators = Resources.FindObjectsOfTypeAll<Animator>();
        for (int i = 0; i < animators.Length; i++)
        {
            Animator a = animators[i];
            if (a == null || a.gameObject == null || !a.gameObject.scene.IsValid() || a.gameObject.scene != activeScene)
            {
                continue;
            }

            if (a.runtimeAnimatorController == null)
            {
                continue;
            }

            string goName = a.gameObject.name;
            if (goName.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                goName.IndexOf("fx", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            AnimationClip[] clips = a.runtimeAnimatorController.animationClips;
            for (int c = 0; c < clips.Length; c++)
            {
                AnimationClip clip = clips[c];
                if (clip == null || string.IsNullOrEmpty(clip.name))
                {
                    continue;
                }

                for (int k = 0; k < keywords.Length; k++)
                {
                    string keyword = keywords[k];
                    if (string.IsNullOrEmpty(keyword))
                    {
                        continue;
                    }

                    if (clip.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        WriteLog("Auto-assigned by clip '" + keyword + "': " + a.gameObject.name + " (" + clip.name + ")");
                        return a.gameObject;
                    }
                }
            }
        }

        return null;
    }

    private void InitializeCharacterPosition()
    {
        if (characterRoot != null)
        {
            _characterWorldPosition = characterRoot.position;
            return;
        }

        if (idleObject != null)
        {
            _characterWorldPosition = idleObject.transform.position;
            return;
        }

        if (runObject != null)
        {
            _characterWorldPosition = runObject.transform.position;
            return;
        }

        if (attackObject != null)
        {
            _characterWorldPosition = attackObject.transform.position;
            return;
        }

        if (samuraiAnimator != null)
        {
            _characterWorldPosition = samuraiAnimator.transform.position;
            return;
        }

        _characterWorldPosition = transform.position;
    }

    private void SetVisualStateForRun()
    {
        SetActiveSafe(idleObject, false);
        SetActiveSafe(runObject, true);
        SetActiveSafe(attackObject, false);
        ApplySceneFxActive(false);
        WriteLog("Visual state -> RUN");
    }

    private void SetVisualStateForIdle()
    {
        SetActiveSafe(idleObject, true);
        SetActiveSafe(runObject, false);
        SetActiveSafe(attackObject, false);
        ApplySceneFxActive(false);
        WriteLog("Visual state -> IDLE");
    }

    private void SetVisualStateForAttack()
    {
        SetActiveSafe(idleObject, false);
        SetActiveSafe(runObject, false);
        SetActiveSafe(attackObject, true);
        ApplySceneFxActive(true);
        WriteLog("Visual state -> ATTACK");
    }

    private void ApplySceneFxActive(bool active)
    {
        if (!forceDisableSceneFxUntilAttack)
        {
            return;
        }

        if (disableAllFxForNow)
        {
            active = false;
        }

        ToggleParticles(idleObject, false);
        ToggleParticles(runObject, false);
        ToggleParticles(attackObject, active);
    }

    private void ToggleParticles(GameObject rootObject, bool active)
    {
        if (rootObject == null)
        {
            return;
        }

        ParticleSystem[] particles = rootObject.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem ps = particles[i];
            if (ps == null)
            {
                continue;
            }

            if (active)
            {
                if (!ps.gameObject.activeSelf)
                {
                    ps.gameObject.SetActive(true);
                }
                ps.Play(true);
            }
            else
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (ps.gameObject.activeSelf)
                {
                    ps.gameObject.SetActive(false);
                }
            }
        }
    }

    private bool HasAnimatorParameter(Animator targetAnimator, string parameterName, AnimatorControllerParameterType expectedType)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(parameterName))
        {
            return false;
        }

        AnimatorControllerParameter[] parameters = targetAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter p = parameters[i];
            if (p != null && p.name == parameterName && p.type == expectedType)
            {
                return true;
            }
        }

        return false;
    }

    private void SetActiveSafe(GameObject target, bool value)
    {
        if (target != null && target.activeSelf != value)
        {
            target.SetActive(value);
        }
    }

    private void ApplyCharacterPositionToStateObjects()
    {
        SyncStateObjectPositions(_characterWorldPosition);
    }

    private void SyncStateObjectPositions(Vector3 worldPos)
    {
        SyncObjectPosition(idleObject, worldPos);
        SyncObjectPosition(runObject, worldPos);
        SyncObjectPosition(attackObject, worldPos);
        if (characterRoot != null)
        {
            Vector3 p = characterRoot.position;
            p.x = worldPos.x;
            characterRoot.position = p;
        }
    }

    private void SyncObjectPosition(GameObject target, Vector3 worldPos)
    {
        if (target == null)
        {
            return;
        }

        Vector3 p = target.transform.position;
        p.x = worldPos.x;
        target.transform.position = p;
    }

    private void DumpRuntimeBindings(string phase)
    {
        WriteLog("BINDINGS[" + phase + "] idle=" + DescribeObject(idleObject));
        WriteLog("BINDINGS[" + phase + "] run=" + DescribeObject(runObject));
        WriteLog("BINDINGS[" + phase + "] attack=" + DescribeObject(attackObject));
        WriteLog("BINDINGS[" + phase + "] samuraiAnimator=" + DescribeAnimator(samuraiAnimator));
        WriteLog("BINDINGS[" + phase + "] idleAnimator=" + DescribeAnimator(_idleAnimator));
        WriteLog("BINDINGS[" + phase + "] runAnimator=" + DescribeAnimator(_runAnimator));
        WriteLog("BINDINGS[" + phase + "] attackAnimator=" + DescribeAnimator(_attackAnimator));
    }

    private string DescribeAnimator(Animator animator)
    {
        if (animator == null)
        {
            return "null";
        }

        return DescribeObject(animator.gameObject) + ", enabled=" + animator.enabled + ", speed=" + animator.speed;
    }

    private string DescribeObject(GameObject go)
    {
        if (go == null)
        {
            return "null";
        }

        return go.name + "(id=" + go.GetInstanceID() + ", activeSelf=" + go.activeSelf + ", activeInHierarchy=" + go.activeInHierarchy + ", path=" + GetHierarchyPath(go.transform) + ")";
    }

    private string GetHierarchyPath(Transform t)
    {
        if (t == null)
        {
            return "null";
        }

        string path = t.name;
        Transform current = t.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private void WriteLog(string message)
    {
        string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
        Debug.Log(line);

        AppendLogSafely(_logFilePath, line);
        AppendLogSafely(_projectLogFilePath, line);
    }

    private void AppendLogSafely(string path, string line)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.AppendAllText(path, line + "\n");
        }
        catch
        {
            // keep console only
        }
    }
}
