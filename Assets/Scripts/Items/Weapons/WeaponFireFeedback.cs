using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class WeaponFireFeedback : MonoBehaviour
{
    [Header("Setup")]
    [SerializeField] Transform muzzle;
    [SerializeField] Transform recoilRoot;
    [SerializeField] Transform mountPoint;
    [SerializeField] ParticleSystem muzzleFlashPrefab;
    [SerializeField] ProceduralMuzzleFlash proceduralMuzzleFlashPrefab;
    [SerializeField] bool parentFlashToMuzzle = true;

    [Header("Recoil")]
    [SerializeField] RecoilMode recoilMode = RecoilMode.MountpointShake;

    [Header("Transform Recoil")]
    [SerializeField, Min(0f)] float recoilKickbackDistance = 0.02f;
    [SerializeField, Min(0f)] float recoilKickDuration = 0.05f;
    [SerializeField, Min(0f)] float recoilReturnDuration = 0.1f;
    [SerializeField, Min(0f)] float recoilUpRotation = 3f;
    [SerializeField, Min(0f)] float recoilLateralJitter = 0.8f;
    [SerializeField] AnimationCurve recoilCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Mountpoint Shake (indirect recoil)")]
    [SerializeField, Min(0f)] float mountpointKickbackDistance = 0.05f;
    [SerializeField, Min(0f)] float mountpointRotation = 1.5f;
    [SerializeField, Min(0f)] float mountpointDuration = 0.12f;
    [SerializeField] AnimationCurve mountpointCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public enum RecoilMode
    {
        TransformKick,
        MountpointShake,
        Both
    }

    ParticleSystem muzzleFlashInstance;
    ProceduralMuzzleFlash proceduralMuzzleFlashInstance;
    Coroutine recoilRoutine;
    Coroutine mountpointRoutine;
    Vector3 initialLocalPosition;
    Quaternion initialLocalRotation;
    Vector3 initialMountLocalPosition;
    Quaternion initialMountLocalRotation;
    bool cachedRecoilDefaults;
    bool cachedMountpointDefaults;

    Transform RecoilRoot => recoilRoot ? recoilRoot : transform;

    void Awake()
    {
        CacheRecoilDefaults();
        CacheMountpointDefaults();
    }

    void OnDisable()
    {
        if (proceduralMuzzleFlashInstance)
            proceduralMuzzleFlashInstance.StopImmediate();

        if (muzzleFlashInstance)
            muzzleFlashInstance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ResetRecoil();
        ResetMountpointShake();
    }

    void CacheRecoilDefaults()
    {
        var root = RecoilRoot;
        if (!root)
            return;

        initialLocalPosition = root.localPosition;
        initialLocalRotation = root.localRotation;
        cachedRecoilDefaults = true;
    }

    void CacheMountpointDefaults()
    {
        var target = mountPoint;
        if (!target)
            return;

        initialMountLocalPosition = target.localPosition;
        initialMountLocalRotation = target.localRotation;
        cachedMountpointDefaults = true;
    }

    public void SetMuzzle(Transform muzzleTransform)
    {
        if (muzzleTransform)
            muzzle = muzzleTransform;

        if (parentFlashToMuzzle && muzzle)
        {
            if (muzzleFlashInstance)
                muzzleFlashInstance.transform.SetParent(muzzle, true);
            if (proceduralMuzzleFlashInstance)
                proceduralMuzzleFlashInstance.transform.SetParent(muzzle, true);
        }
    }

    public void SetMountPoint(Transform mount)
    {
        if (mount)
        {
            if (mountpointRoutine != null)
            {
                StopCoroutine(mountpointRoutine);
                mountpointRoutine = null;
            }

            ResetMountpointShake();

            mountPoint = mount;
        }

        cachedMountpointDefaults = false;
        CacheMountpointDefaults();
    }

    public void Play()
    {
        PlayMuzzleFlash();
        PlayRecoil();
    }

    void PlayMuzzleFlash()
    {
        if (!muzzle)
            return;

        if (proceduralMuzzleFlashPrefab)
        {
            if (!proceduralMuzzleFlashInstance)
            {
                proceduralMuzzleFlashInstance = Instantiate(proceduralMuzzleFlashPrefab, muzzle.position, muzzle.rotation);
                if (parentFlashToMuzzle)
                    proceduralMuzzleFlashInstance.transform.SetParent(muzzle, true);
            }

            var proceduralFlashTransform = proceduralMuzzleFlashInstance.transform;
            proceduralFlashTransform.position = muzzle.position;
            proceduralFlashTransform.rotation = muzzle.rotation;
            proceduralMuzzleFlashInstance.Play();
            return;
        }

        if (!muzzleFlashPrefab)
            return;

        if (!muzzleFlashInstance)
        {
            muzzleFlashInstance = Instantiate(muzzleFlashPrefab, muzzle.position, muzzle.rotation);
            if (parentFlashToMuzzle)
                muzzleFlashInstance.transform.SetParent(muzzle, true);
        }

        var particleFlashTransform = muzzleFlashInstance.transform;
        particleFlashTransform.position = muzzle.position;
        particleFlashTransform.rotation = muzzle.rotation;

        muzzleFlashInstance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        muzzleFlashInstance.Play(true);
    }

    void PlayRecoil()
    {
        if (!isActiveAndEnabled)
            return;

        if (!cachedRecoilDefaults)
            CacheRecoilDefaults();
        if (!cachedMountpointDefaults)
            CacheMountpointDefaults();

        bool wantsTransformRecoil = (recoilMode == RecoilMode.TransformKick || recoilMode == RecoilMode.Both) && RecoilRoot;
        bool wantsMountpointShake = (recoilMode == RecoilMode.MountpointShake || recoilMode == RecoilMode.Both) && mountPoint;

        if (!wantsTransformRecoil && recoilMode == RecoilMode.TransformKick && mountPoint)
            wantsMountpointShake = true; // fallback when we can't move the weapon

        if (wantsTransformRecoil)
        {
            if (recoilRoutine != null)
                StopCoroutine(recoilRoutine);

            recoilRoutine = StartCoroutine(RecoilRoutine());
        }

        if (!wantsTransformRecoil && !wantsMountpointShake)
            return;

        if (wantsMountpointShake)
        {
            if (mountpointRoutine != null)
            {
                StopCoroutine(mountpointRoutine);
                ResetMountpointShake();
            }

            mountpointRoutine = StartCoroutine(MountpointShakeRoutine());
        }
    }

    IEnumerator RecoilRoutine()
    {
        var root = RecoilRoot;
        if (!root)
            yield break;

        Vector3 startPos = initialLocalPosition;
        Quaternion startRot = initialLocalRotation;
        Vector3 recoilPos = startPos + Vector3.back * recoilKickbackDistance;

        Vector3 randomEuler = new Vector3(
            -recoilUpRotation,
            Random.Range(-recoilLateralJitter, recoilLateralJitter),
            Random.Range(-recoilLateralJitter * 0.5f, recoilLateralJitter * 0.5f));

        Quaternion recoilRot = startRot * Quaternion.Euler(randomEuler);

        float elapsed = 0f;
        float duration = Mathf.Max(0.0001f, recoilKickDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curved = recoilCurve.Evaluate(t);
            root.localPosition = Vector3.Lerp(startPos, recoilPos, curved);
            root.localRotation = Quaternion.Slerp(startRot, recoilRot, curved);
            yield return null;
        }

        elapsed = 0f;
        duration = Mathf.Max(0.0001f, recoilReturnDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            root.localPosition = Vector3.Lerp(recoilPos, startPos, t);
            root.localRotation = Quaternion.Slerp(recoilRot, startRot, t);
            yield return null;
        }

        ResetRecoil();
        recoilRoutine = null;
    }

    void ResetRecoil()
    {
        var root = RecoilRoot;
        if (!root || !cachedRecoilDefaults)
            return;

        root.localPosition = initialLocalPosition;
        root.localRotation = initialLocalRotation;
    }

    IEnumerator MountpointShakeRoutine()
    {
        var target = mountPoint;
        if (!target)
            yield break;

        Vector3 startPos = initialMountLocalPosition;
        Quaternion startRot = initialMountLocalRotation;
        Vector3 recoilPos = startPos + Vector3.back * mountpointKickbackDistance;

        Vector3 randomEuler = new Vector3(
            -mountpointRotation,
            Random.Range(-mountpointRotation, mountpointRotation),
            Random.Range(-mountpointRotation * 0.5f, mountpointRotation * 0.5f));

        Quaternion recoilRot = startRot * Quaternion.Euler(randomEuler);

        float elapsed = 0f;
        float duration = Mathf.Max(0.0001f, mountpointDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curved = mountpointCurve.Evaluate(t);
            target.localPosition = Vector3.Lerp(startPos, recoilPos, curved);
            target.localRotation = Quaternion.Slerp(startRot, recoilRot, curved);
            yield return null;
        }

        ResetMountpointShake();
        mountpointRoutine = null;
    }

    void ResetMountpointShake()
    {
        var target = mountPoint;
        if (!target || !cachedMountpointDefaults)
            return;

        target.localPosition = initialMountLocalPosition;
        target.localRotation = initialMountLocalRotation;
    }
}
