using System.IO;
using UnityEngine;

public class FxManager : MonoBehaviour
{
    private static FxManager _instance;
    private string _logFilePath;

    public static FxManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<FxManager>();
            }

            if (_instance == null)
            {
                GameObject fxManagerObject = new GameObject("FxManager");
                _instance = fxManagerObject.AddComponent<FxManager>();
            }

            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        _logFilePath = Path.Combine(Application.persistentDataPath, "samurai_attack_log.txt");
        WriteLog("FxManager Awake");
    }

    public void RequestSamuraiSlashFx(Vector3 spawnPosition, GameObject fxSource)
    {
        if (fxSource == null)
        {
            WriteLog("WARN: fxSource is null. FX spawn skipped.");
            return;
        }

        GameObject fxObject = Instantiate(fxSource, spawnPosition, fxSource.transform.rotation);
        fxObject.name = "fx_samurai_slash";
        ForceFxVisible(fxObject);
        PlayFxAnimationNow(fxObject);
        fxObject.SetActive(true);
        WriteLog("Created fx_samurai_slash at " + spawnPosition);
        StartCoroutine(DestroyAfterPlay(fxObject));
    }

    private System.Collections.IEnumerator DestroyAfterPlay(GameObject fxObject)
    {
        float duration = 0.7f;

        Animator animator = fxObject.GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].length > duration)
                {
                    duration = clips[i].length;
                }
            }
        }

        ParticleSystem particle = fxObject.GetComponentInChildren<ParticleSystem>();
        if (particle != null)
        {
            float particleDuration = particle.main.duration + particle.main.startLifetime.constantMax;
            if (particleDuration > duration)
            {
                duration = particleDuration;
            }
        }

        yield return new WaitForSeconds(duration);
        Destroy(fxObject);
        WriteLog("Destroyed fx_samurai_slash after " + duration + "s");
    }

    private void WriteLog(string message)
    {
        string line = "[" + System.DateTime.Now.ToString("HH:mm:ss") + "] " + message;
        Debug.Log(line);

        if (string.IsNullOrEmpty(_logFilePath))
        {
            return;
        }

        try
        {
            File.AppendAllText(_logFilePath, line + "\n");
        }
        catch
        {
            // Keep console log only when file writing fails.
        }
    }

    private void ForceFxVisible(GameObject fxObject)
    {
        SpriteRenderer[] renderers = fxObject.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer sr = renderers[i];
            if (sr == null)
            {
                continue;
            }

            sr.enabled = true;
            sr.sortingOrder = 50;
            if (!sr.gameObject.activeSelf)
            {
                sr.gameObject.SetActive(true);
            }
        }
    }

    private void PlayFxAnimationNow(GameObject fxObject)
    {
        Animator animator = fxObject.GetComponent<Animator>();
        if (animator != null)
        {
            animator.Play(0, 0, 0f);
            animator.Update(0f);
        }

        Animation legacy = fxObject.GetComponent<Animation>();
        if (legacy != null)
        {
            legacy.Stop();
            legacy.Play();
        }
    }
}
