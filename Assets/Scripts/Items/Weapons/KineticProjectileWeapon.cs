using FishNet.Object;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public abstract class KineticProjectileWeapon : NetworkBehaviour, IToolbeltItemCategoryProvider, IWeapon
{
    [Header("Refs")]
    [SerializeField] protected Transform muzzle;
    [SerializeField] protected Transform mountPoint;
    [SerializeField] protected Camera cam;
    [SerializeField] protected LineRenderer aimBeam;

    [Header("Feedback")]
    [SerializeField] protected WeaponFireFeedback fireFeedback;

    [Header("Ballistics")]
    [SerializeField] protected float bulletSpeed = 25f;
    [SerializeField] protected float damage = 1f;
    [SerializeField] protected float force = 1f;
    [Tooltip("Seconds between shots (e.g. 0.2 = 5 rps)")]
    [SerializeField] protected float fireCooldown = 0.2f;

    [Header("Accuracy")]
    [Tooltip("Maximum inaccuracy cone angle (degrees) when shooting skill is at minimum.")]
    [SerializeField, Min(0f)] protected float maxInaccuracyAngle = 6f;

    [Header("Hit Detection")]
    [SerializeField, Min(0f)] protected float bulletRadius = 0.06f;
    [SerializeField, Min(0f)] protected float bulletRange = 75f;
    [SerializeField] protected LayerMask bulletHitMask;

    [Header("Range")]
    [SerializeField] protected WeaponRange weaponRange = new WeaponRange(0f, 10f, 25f, 50f, 100f);

    [Header("Tracer Visuals")]
    [SerializeField] protected Color tracerColor = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField, Min(0f)] protected float tracerDuration = 0.05f;
    [SerializeField, Min(0f)] protected float tracerWidth = 0.01f;
    [SerializeField] protected Material tracerMaterial;

    [Header("Mag / Reload")]
    [SerializeField] protected int magSize = 12;
    [SerializeField] protected float reloadTime = 1.2f;
    [SerializeField] protected bool isAutomatic = false;

    [Header("Aiming")]
    [SerializeField] protected LayerMask aimMask;
    [SerializeField] protected float aimMaxDistance = 1000f;
    [SerializeField] protected float fallbackAimDistance = 50f;
    [SerializeField, Min(0f)] protected float aimBeamWidth = 0.01f;
    [SerializeField, Min(0f)] protected float aimBeamImpactSize = 0.08f;
    [SerializeField] protected Color aimBeamImpactColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] protected Color aimBeamImpactMisalignedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
    [SerializeField, Min(0f)] protected float aimMuzzleMaxAngle = 8f;
    [SerializeField] protected Color aimBeamAlignedColor = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] protected Color aimBeamMisalignedColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    // NEW: allow a separate interaction to drive input
    [Header("Control")]
    [SerializeField] protected bool driveByInteraction = true;

    [Header("Equipping")]
    [SerializeField, Min(0f)] protected float equipDuration = 0.3f;
    [SerializeField, Min(0f)] protected float unequipDuration = 0.25f;
    [SerializeField, Min(0f)] protected float stanceTransitionDuration = 0.1f;

    float nextFireTime;
    NetworkObject ownerIdentity;
    TopDownMotor ownerMotor;
    CharacterStats ownerStats;

    protected int currentInMag;
    bool reloading;
    float reloadFinishAt;
    ToolbeltNetworked ownerToolbelt;

    Transform aimImpactTransform;
    SpriteRenderer aimImpactSprite;

    static Texture2D aimImpactTexture;
    static Sprite aimImpactSpriteAsset;
    LineRenderer tracerRenderer;
    Coroutine tracerRoutine;
    Material tracerMaterialInstance;

    const float DefaultProjectileTravelTime = 3f;

    public bool IsAutomatic => isAutomatic;                   // < for interaction
    public void InteractionSetCamera(Camera c) { if (!cam) cam = c; }  // <
    public virtual ToolbeltSlotType ToolbeltCategory => ToolbeltSlotType.Primary;
    public virtual ToolMountPoint.MountType ToolbeltMountType => ToolMountPoint.MountType.SmallRangedWeapon;
    public virtual float ToolbeltEquipDuration => Mathf.Max(0f, equipDuration);
    public virtual float ToolbeltUnequipDuration => Mathf.Max(0f, unequipDuration);
    public virtual float ToolbeltStanceTransitionDuration => Mathf.Max(0f, stanceTransitionDuration);
    public virtual float ReloadDuration => Mathf.Max(0f, reloadTime);
    public float ProjectileRadius => Mathf.Max(0f, bulletRadius);
    public float ProjectileRange => bulletRange > 0f ? bulletRange : bulletSpeed * DefaultProjectileTravelTime;
    public LayerMask ProjectileHitMask => bulletHitMask;
    public float ProjectileForce => Mathf.Max(0f, force);
    public WeaponRange WeaponRange => weaponRange;

    public void SetMountPoint(Transform mount)
    {
        var resolvedMount = mount ? mount : transform.parent;
        mountPoint = resolvedMount;

        if (fireFeedback)
            fireFeedback.SetMountPoint(resolvedMount);
    }

    protected virtual void Awake()
    {
        ownerIdentity = transform.root.GetComponent<NetworkObject>();
        if (!ownerToolbelt)
            ownerToolbelt = transform.root.GetComponentInChildren<ToolbeltNetworked>(true);
        if (!ownerMotor)
            ownerMotor = transform.root.GetComponentInChildren<TopDownMotor>(true);
        if (!ownerStats)
            ownerStats = transform.root.GetComponentInChildren<CharacterStats>(true);
        currentInMag = Mathf.Clamp(magSize, 0, int.MaxValue);
        reloading = false;

        if (!fireFeedback)
            fireFeedback = GetComponentInChildren<WeaponFireFeedback>(true);
        SetMountPoint(mountPoint);
        if (fireFeedback)
            fireFeedback.SetMuzzle(muzzle);

        ApplyAimBeamWidth();
        ApplyTracerAppearance();
    }

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        if (!fireFeedback)
            fireFeedback = GetComponentInChildren<WeaponFireFeedback>(true);
        SetMountPoint(mountPoint);
        if (fireFeedback)
            fireFeedback.SetMuzzle(muzzle);

        ApplyAimBeamWidth();
        ApplyTracerAppearance();
    }
#endif

    protected virtual void OnEnable()
    {
        if (!driveByInteraction && IsLocalOwner() && !cam) ResolveCamera();
    }

    protected virtual void OnDisable()
    {
        DisableAimVisuals();
        HideTracerImmediate();
    }

    protected virtual void Update()
    {
        UpdateAimBeam();

        // If an interaction drives us, don't read input here
        if (driveByInteraction) return;

        if (!IsLocalOwner() || Mouse.current == null) return;

        // reload timing
        if (reloading && Time.time >= reloadFinishAt)
        {
            reloading = false;
            currentInMag = magSize;
            OnReloadCompleted();
        }

        if (!reloading && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            BeginReload();

        bool wantsShot = isAutomatic
            ? Mouse.current.leftButton.isPressed
            : Mouse.current.leftButton.wasPressedThisFrame;

        if (wantsShot) TryFire();
    }

    bool IsLocalOwner() => (ownerIdentity != null && ownerIdentity.IsOwner);

    // NEW: external tick from interaction (owner-side)
    public void InteractionTick(bool triggerPressed, bool reloadPressed)
    {
        // Allow server-authority callers (e.g., NPCs) in addition to the owning client.
        if (!IsOwner && OwnerId != -1)
            return;

        if (reloading && Time.time >= reloadFinishAt)
        {
            reloading = false;
            currentInMag = magSize;
            OnReloadCompleted();
        }

        if (!reloading && reloadPressed)
            BeginReload();

        if (triggerPressed)
            TryFire();
    }

    // made protected so interaction can call via InteractionTick
    protected void TryFire()
    {
        if (reloading) return;
        if (Time.time < nextFireTime) return;
        if (!muzzle) return;

        if (currentInMag <= 0)
        {
            BeginReload();
            return;
        }

        if (!ComputeShot(out Vector3 origin, out Vector3 dir))
            return;

        if (IsMuzzleMisaligned(dir))
            return;

        currentInMag = Mathf.Max(0, currentInMag - 1);
        nextFireTime = Time.time + fireCooldown;

        OnLocalFired(origin, dir);
        ownerToolbelt?.RequestFireProjectile(this, origin, dir, bulletSpeed, damage, ProjectileForce);

        if (currentInMag == 0)
            BeginReload();
    }

    protected void BeginReload()
    {
        if (reloading) return;
        if (currentInMag == magSize) return;

        reloading = true;
        reloadFinishAt = Time.time + Mathf.Max(0f, reloadTime);
        OnReloadStarted();
    }

    protected virtual void OnReloadStarted()
    {
        ownerToolbelt?.NotifyEquippedWeaponReloadState(this, true);
    }

    protected virtual void OnReloadCompleted()
    {
        ownerToolbelt?.NotifyEquippedWeaponReloadState(this, false);
    }

    void EnsureTracerRenderer()
    {
        if (tracerRenderer)
            return;

        var go = new GameObject("BulletTracer");
        go.transform.SetParent(transform, false);
        tracerRenderer = go.AddComponent<LineRenderer>();
        tracerRenderer.enabled = false;
        tracerRenderer.positionCount = 2;
        tracerRenderer.useWorldSpace = true;
        tracerRenderer.loop = false;
        tracerRenderer.textureMode = LineTextureMode.Stretch;
        tracerRenderer.shadowCastingMode = ShadowCastingMode.Off;
        tracerRenderer.receiveShadows = false;
        tracerRenderer.alignment = LineAlignment.View;

        if (tracerMaterial)
        {
            tracerMaterialInstance = new Material(tracerMaterial);
        }
        else
        {
            var shader = Shader.Find("Sprites/Default");
            tracerMaterialInstance = shader ? new Material(shader) : null;
        }

        if (tracerMaterialInstance)
            tracerRenderer.material = tracerMaterialInstance;

        ApplyTracerAppearance();
    }

    void ApplyTracerAppearance()
    {
        if (tracerRenderer)
        {
            float width = Mathf.Max(0.0001f, tracerWidth);
            tracerRenderer.startWidth = width;
            tracerRenderer.endWidth = width;
            tracerRenderer.startColor = tracerColor;
            tracerRenderer.endColor = tracerColor;
        }

        if (tracerMaterialInstance)
            tracerMaterialInstance.color = tracerColor;
    }

    void ShowTracer(Vector3 start, Vector3 end)
    {
        if (tracerDuration <= 0f)
            return;

        EnsureTracerRenderer();
        if (!tracerRenderer)
            return;

        tracerRenderer.SetPosition(0, start);
        tracerRenderer.SetPosition(1, end);

        if (tracerRoutine != null)
        {
            StopCoroutine(tracerRoutine);
            tracerRoutine = null;
        }

        tracerRoutine = StartCoroutine(TracerRoutine());
    }

    IEnumerator TracerRoutine()
    {
        if (tracerRenderer)
            tracerRenderer.gameObject.SetActive(true);

        yield return new WaitForSeconds(tracerDuration);

        if (tracerRenderer)
            tracerRenderer.gameObject.SetActive(false);

        tracerRoutine = null;
    }

    void HideTracerImmediate()
    {
        if (tracerRoutine != null)
        {
            StopCoroutine(tracerRoutine);
            tracerRoutine = null;
        }

        if (tracerRenderer)
            tracerRenderer.gameObject.SetActive(false);
    }

    protected virtual void ResolveCamera()
    {
        var root = transform.root;
        if (root) cam = root.GetComponentInChildren<Camera>(true);
    }

    protected virtual bool ComputeShot(out Vector3 origin, out Vector3 dir)
    {
        if (!TryComputeAim(out origin, out dir, out _))
            return false;

        dir = ApplyAccuracy(dir);
        return true;
    }

    bool TryComputeAim(out Vector3 origin, out Vector3 dir, out Vector3 target)
    {
        origin = Vector3.zero;
        dir = Vector3.forward;
        target = Vector3.zero;

        if (!muzzle)
            return false;

        origin = muzzle.position;

        if (!ownerMotor || ownerMotor.transform.root != transform.root)
            ownerMotor = transform.root.GetComponentInChildren<TopDownMotor>(true);

        Vector3 aimPoint;
        if (!TryGetCharacterTarget(out aimPoint) && !TryGetCameraTarget(out aimPoint))
            aimPoint = origin + muzzle.forward * Mathf.Max(0.1f, fallbackAimDistance);

        Vector3 toAim = aimPoint - origin;
        float sqr = toAim.sqrMagnitude;
        if (sqr < 0.0001f)
            return false;

        float distance = Mathf.Sqrt(sqr);
        Vector3 normalized = toAim / distance;

        float maxDistance = aimMaxDistance > 0f ? aimMaxDistance : distance;
        if (distance > maxDistance)
        {
            aimPoint = origin + normalized * maxDistance;
            distance = maxDistance;
        }

        int mask = aimMask.value != 0 ? aimMask.value : Physics.DefaultRaycastLayers;
        var hits = Physics.RaycastAll(origin, normalized, distance, mask, QueryTriggerInteraction.Ignore);
        float bestDistance = distance;
        Vector3 bestPoint = aimPoint;

        foreach (var hit in hits)
        {
            if (!hit.transform)
                continue;

            if (hit.transform == transform || hit.transform.IsChildOf(transform.root))
                continue;

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                bestPoint = hit.point;
            }
        }

        target = bestPoint;

        Vector3 finalDir = target - origin;
        float finalSqr = finalDir.sqrMagnitude;
        if (finalSqr < 0.0001f)
            return false;

        dir = finalDir / Mathf.Sqrt(finalSqr);
        return true;
    }

    bool TryGetCharacterTarget(out Vector3 target)
    {
        target = Vector3.zero;

        if (!ownerMotor)
            return false;

        if (!ownerMotor.HasCursorTarget)
            return false;

        target = ownerMotor.PlayerTarget;
        return true;
    }

    bool TryGetCameraTarget(out Vector3 target)
    {
        target = Vector3.zero;

        if (!cam)
            ResolveCamera();

        if (!cam)
            return false;

        var mouse = Mouse.current;
        if (mouse == null)
        {
            target = cam.transform.position + cam.transform.forward * Mathf.Max(0.1f, fallbackAimDistance);
            return true;
        }

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        int mask = aimMask.value != 0 ? aimMask.value : Physics.DefaultRaycastLayers;

        if (Physics.Raycast(ray, out RaycastHit hit, aimMaxDistance, mask, QueryTriggerInteraction.Ignore))
        {
            target = hit.point;
            return true;
        }

        target = ray.origin + ray.direction * Mathf.Max(0.1f, fallbackAimDistance);
        return true;
    }

    void EnsureOwnerStats()
    {
        if (ownerStats && ownerStats.transform && ownerStats.transform.root == transform.root)
            return;

        ownerStats = transform.root.GetComponentInChildren<CharacterStats>(true);
    }

    float GetInaccuracyAngle()
    {
        EnsureOwnerStats();

        int shootingLevel = ownerStats ? ownerStats.ShootingLevel : CharacterStats.MaxLevel;
        float normalizedSkill = Mathf.InverseLerp(CharacterStats.MinLevel, CharacterStats.MaxLevel, shootingLevel);
        float inaccuracyFactor = 1f - normalizedSkill;
        return Mathf.Max(0f, maxInaccuracyAngle) * inaccuracyFactor;
    }

    Vector3 ApplyAccuracy(Vector3 direction)
    {
        float angle = GetInaccuracyAngle();
        if (angle <= 0f || direction.sqrMagnitude < 0.0001f)
            return direction.normalized;

        Vector3 randomAxis = Vector3.Cross(direction, Random.onUnitSphere);
        if (randomAxis.sqrMagnitude < 0.0001f)
            randomAxis = Vector3.Cross(direction, Vector3.up);

        randomAxis = randomAxis.normalized;
        float appliedAngle = Random.Range(0f, angle);
        Quaternion rotation = Quaternion.AngleAxis(appliedAngle, randomAxis);
        return (rotation * direction).normalized;
    }

    void UpdateAimBeam()
    {
        if (!IsLocalOwner())
        {
            DisableAimVisuals();
            return;
        }

        if (!aimBeam && muzzle)
            EnsureAimBeam();

        if (!aimBeam)
            return;

        EnsureAimImpactMarker();

        if (!ownerToolbelt || ownerToolbelt.transform.root != transform.root)
            ownerToolbelt = transform.root.GetComponentInChildren<ToolbeltNetworked>(true);

        if (ownerToolbelt && ownerToolbelt.ActiveWeapon != this)
        {
            DisableAimVisuals();
            return;
        }

        if (!ownerMotor || ownerMotor.transform.root != transform.root)
            ownerMotor = transform.root.GetComponentInChildren<TopDownMotor>(true);

        if (!ownerMotor || ownerMotor.CurrentStance != TopDownMotor.Stance.Active)
        {
            DisableAimVisuals();
            return;
        }

        if (!TryComputeAim(out Vector3 origin, out _, out Vector3 target))
        {
            DisableAimVisuals();
            return;
        }

        Vector3 toTarget = target - origin;
        float sqr = toTarget.sqrMagnitude;
        if (sqr < 0.0001f)
        {
            DisableAimVisuals();
            return;
        }

        float distance = Mathf.Sqrt(sqr);
        Vector3 direction = toTarget / distance;
        float maxDistance = aimMaxDistance > 0f ? aimMaxDistance : distance;

        Vector3 endPoint = origin + direction * maxDistance;
        Vector3 hitNormal = -direction;
        bool hasImpact = false;

        int mask = aimMask.value != 0 ? aimMask.value : Physics.DefaultRaycastLayers;
        var hits = Physics.RaycastAll(origin, direction, maxDistance, mask, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (!hit.transform)
                    continue;

                if (hit.transform == transform || hit.transform.IsChildOf(transform.root))
                    continue;

                endPoint = hit.point;
                hitNormal = hit.normal;
                hasImpact = true;
                break;
            }
        }

        aimBeam.gameObject.SetActive(true);
        aimBeam.positionCount = 2;
        aimBeam.useWorldSpace = true;
        aimBeam.SetPosition(0, origin);
        aimBeam.SetPosition(1, endPoint);

        bool misaligned = IsMuzzleMisaligned(direction);
        ApplyAimBeamColor(misaligned ? aimBeamMisalignedColor : aimBeamAlignedColor);

        UpdateAimImpactMarker(hasImpact, endPoint, hitNormal, misaligned);
    }

    void DisableAimVisuals()
    {
        if (aimBeam && aimBeam.gameObject.activeSelf)
            aimBeam.gameObject.SetActive(false);

        if (aimImpactSprite && aimImpactSprite.enabled)
            aimImpactSprite.enabled = false;
    }

    void UpdateAimImpactMarker(bool hasImpact, Vector3 point, Vector3 normal, bool misaligned)
    {
        if (!aimBeam)
            return;

        EnsureAimImpactMarker();

        if (!aimImpactSprite)
            return;

        if (!hasImpact)
        {
            if (aimImpactSprite.enabled)
                aimImpactSprite.enabled = false;
            return;
        }

        if (misaligned)
        {
            if (aimImpactSprite.enabled)
                aimImpactSprite.enabled = false;
            return;
        }

        if (!aimImpactTransform)
            aimImpactTransform = aimImpactSprite.transform;

        aimImpactTransform.position = point;

        if (cam)
        {
            aimImpactTransform.rotation = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);
        }
        else if (Camera.main)
        {
            var mainCam = Camera.main;
            aimImpactTransform.rotation = Quaternion.LookRotation(-mainCam.transform.forward, mainCam.transform.up);
        }
        else if (normal.sqrMagnitude > 0.0001f)
        {
            aimImpactTransform.rotation = Quaternion.LookRotation(normal, Vector3.up);
        }

        aimImpactTransform.localScale = Vector3.one * Mathf.Max(0.0001f, aimBeamImpactSize);

        aimImpactSprite.color = misaligned ? aimBeamImpactMisalignedColor : aimBeamImpactColor;

        if (!aimImpactSprite.enabled)
            aimImpactSprite.enabled = true;
    }

    void EnsureAimBeam()
    {
        if (aimBeam || !muzzle)
            return;

        aimBeam = muzzle.GetComponentInChildren<LineRenderer>();
        if (aimBeam)
        {
            ApplyAimBeamWidth();
            ApplyAimBeamColor(aimBeamAlignedColor);
            EnsureAimImpactMarker();
            return;
        }

        var beamObject = new GameObject("AimBeam");
        beamObject.transform.SetParent(muzzle, false);
        beamObject.transform.localPosition = Vector3.zero;
        beamObject.transform.localRotation = Quaternion.identity;

        aimBeam = beamObject.AddComponent<LineRenderer>();
        aimBeam.positionCount = 2;
        aimBeam.useWorldSpace = true;
        ApplyAimBeamWidth();
        aimBeam.gameObject.SetActive(false);

        var shader = Shader.Find("Sprites/Default");
        if (shader)
            aimBeam.material = new Material(shader);

        ApplyAimBeamColor(aimBeamAlignedColor);

        EnsureAimImpactMarker();
    }

    void ApplyAimBeamWidth()
    {
        if (!aimBeam)
            return;

        float width = Mathf.Max(0f, aimBeamWidth);
        aimBeam.startWidth = width;
        aimBeam.endWidth = width;
    }

    void EnsureAimImpactMarker()
    {
        if (!aimBeam)
            return;

        if (aimImpactSprite && aimImpactSprite.gameObject)
            return;

        var impactObject = new GameObject("AimBeamImpact");
        impactObject.transform.SetParent(aimBeam.transform, false);
        impactObject.transform.localPosition = Vector3.zero;
        impactObject.transform.localRotation = Quaternion.identity;

        aimImpactTransform = impactObject.transform;
        aimImpactSprite = impactObject.AddComponent<SpriteRenderer>();
        aimImpactSprite.sprite = GetAimImpactSprite();
        aimImpactSprite.color = aimBeamImpactColor;
        aimImpactSprite.enabled = false;
        aimImpactSprite.sortingOrder = 10;
    }

    static Sprite GetAimImpactSprite()
    {
        if (aimImpactSpriteAsset)
            return aimImpactSpriteAsset;

        if (!aimImpactTexture)
        {
            const int size = 32;
            aimImpactTexture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Vector2 center = new Vector2(size - 1, size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pos = new Vector2(x, y);
                    float dist = Vector2.Distance(pos, center) / (size * 0.5f);
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha *= alpha;
                    aimImpactTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            aimImpactTexture.Apply();
        }

        aimImpactSpriteAsset = Sprite.Create(aimImpactTexture, new Rect(0f, 0f, aimImpactTexture.width, aimImpactTexture.height), new Vector2(0.5f, 0.5f));
        aimImpactSpriteAsset.name = "AimBeamImpactSprite";

        return aimImpactSpriteAsset;
    }

    bool IsMuzzleMisaligned(Vector3 aimDirection)
    {
        if (!muzzle)
            return false;

        float maxAngle = Mathf.Max(0f, aimMuzzleMaxAngle);
        if (maxAngle <= 0f)
            return false;

        if (aimDirection.sqrMagnitude < 0.000001f)
            return false;

        Vector3 referenceAxis = muzzle.forward;
        Vector3 normalizedAim = aimDirection.normalized;
        if (referenceAxis.sqrMagnitude < 0.000001f)
            return false;

        float angle = Vector3.Angle(referenceAxis, normalizedAim);
        return angle > maxAngle;
    }

    void ApplyAimBeamColor(Color color)
    {
        if (!aimBeam)
            return;

        aimBeam.startColor = color;
        aimBeam.endColor = color;

        var material = aimBeam.material;
        if (!material)
            return;

        if (material.HasProperty("_Color"))
            material.color = color;

        if (material.HasProperty("_TintColor"))
            material.SetColor("_TintColor", color);
    }

    protected virtual void OnLocalFired(Vector3 origin, Vector3 dir)
    {
        PlayFireFeedback();
    }

    public virtual void OnServerFired(Vector3 origin, Vector3 endPoint, Vector3 hitNormal, bool hitSomething, bool suppressLocalFeedback)
    {
        if (!suppressLocalFeedback)
            PlayFireFeedback();

        ShowTracer(origin, endPoint);
    }

    void PlayFireFeedback()
    {
        fireFeedback?.Play();
    }

    public void SetOwnerToolbelt(ToolbeltNetworked toolbelt)
    {
        ownerToolbelt = toolbelt;
        if (!ownerIdentity)
            ownerIdentity = transform.root.GetComponent<NetworkObject>();
    }

    public void SetAutomatic(bool auto) => isAutomatic = auto;

    public void AddAmmoToMag(int amount)
    {
        if (amount <= 0) return;
        currentInMag = Mathf.Clamp(currentInMag + amount, 0, magSize);
    }

    public void FinishReloadNow()
    {
        if (!reloading) return;
        reloading = false;
        currentInMag = magSize;
        OnReloadCompleted();
    }

    public (int current, int capacity, bool isReloading, float reloadRemaining) GetMagState()
    {
        float remain = reloading ? Mathf.Max(0f, reloadFinishAt - Time.time) : 0f;
        return (currentInMag, magSize, reloading, remain);
    }
}
