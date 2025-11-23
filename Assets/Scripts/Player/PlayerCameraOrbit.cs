using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[DisallowMultipleComponent]
public class PlayerCameraOrbit : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] TopDownMotor motor;
    [SerializeField] Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Orbit")]
    [SerializeField] float rotationSpeed = 0.2f;
    [SerializeField] float minPitch = 10f;
    [SerializeField] float maxPitch = 80f;

    [Header("Zoom")]
    [SerializeField] float zoomSpeed = 0.5f;
    [SerializeField] float minDistance = 3f;
    [SerializeField] float maxDistance = 15f;

    [Header("Focus")]
    [SerializeField] float focusSmoothTime = 0.15f;
    [SerializeField, Range(0f, 1f)] float activeFocusCenterRadius = 0.2f;
    [SerializeField] float activeFocusEdgeResponse = 2f;
    [SerializeField] float maxActiveFocusDistance = 6f;

    float yaw;
    float pitch;
    float distance;
    NetworkObject networkObject;
    Vector3 focusPosition;
    Vector3 focusVelocity;
    Vector3 lastCursorFocus;
    bool hasCursorFocus;
    bool wasActiveFocus;
    Camera orbitCamera;
    bool orbitCursorLocked;
    Vector2 orbitCursorLockPosition;

    void Awake()
    {
        networkObject = GetComponentInParent<NetworkObject>();

        if (!target && transform.parent)
        {
            target = transform.parent;
        }

        if (!motor && target)
        {
            motor = target.GetComponentInParent<TopDownMotor>();
        }

        if (!motor)
        {
            motor = GetComponentInParent<TopDownMotor>();
        }

        orbitCamera = GetComponent<Camera>();
        if (networkObject && !networkObject.IsOwner && orbitCamera)
        {
            orbitCamera.enabled = false;
            enabled = false;
            return;
        }

        focusPosition = ComputePlayerFocus();
        lastCursorFocus = focusPosition;
        hasCursorFocus = false;
        focusVelocity = Vector3.zero;

        ResetFromCurrentTransform();
    }

    void OnValidate()
    {
        minPitch = Mathf.Clamp(minPitch, -89f, 89f);
        maxPitch = Mathf.Clamp(maxPitch, -89f, 89f);
        if (maxPitch < minPitch)
        {
            maxPitch = minPitch;
        }

        if (maxDistance < minDistance)
        {
            maxDistance = minDistance;
        }

        if (focusSmoothTime < 0f)
        {
            focusSmoothTime = 0f;
        }

        if (maxActiveFocusDistance < 0f)
        {
            maxActiveFocusDistance = 0f;
        }

        activeFocusCenterRadius = Mathf.Clamp01(activeFocusCenterRadius);

        if (activeFocusEdgeResponse < 0.01f)
        {
            activeFocusEdgeResponse = 0.01f;
        }

        if (!motor)
        {
            motor = GetComponentInParent<TopDownMotor>();
        }
    }

    void LateUpdate()
    {
        if (!target)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.middleButton.wasPressedThisFrame)
            {
                orbitCursorLocked = TryGetPointerPosition(out orbitCursorLockPosition);
            }

            if (mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * rotationSpeed;
                pitch -= delta.y * rotationSpeed;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
            else if (mouse.middleButton.wasReleasedThisFrame)
            {
                if (orbitCursorLocked)
                {
                    WarpCursorToLock(mouse);
                }

                orbitCursorLocked = false;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > Mathf.Epsilon)
            {
                distance -= scroll * zoomSpeed;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }

            if (mouse.middleButton.isPressed && orbitCursorLocked)
            {
                WarpCursorToLock(mouse);
            }
        }

        UpdateFocus();
        ApplyOrbit();
    }

    void ResetFromCurrentTransform()
    {
        if (!target)
        {
            return;
        }

        focusPosition = ComputePlayerFocus();
        lastCursorFocus = focusPosition;
        hasCursorFocus = false;
        focusVelocity = Vector3.zero;

        Vector3 focus = GetFocusPosition();
        Vector3 offset = transform.position - focus;
        distance = offset.magnitude;

        if (distance > 0.0001f)
        {
            Vector3 direction = offset / distance;
            direction.y = Mathf.Clamp(direction.y, -0.9999f, 0.9999f);
            pitch = Mathf.Asin(direction.y) * Mathf.Rad2Deg;
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }
        else
        {
            Vector3 euler = transform.eulerAngles;
            pitch = euler.x;
            yaw = euler.y;
        }

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        ApplyOrbit();
    }

    void UpdateFocus()
    {
        Vector3 playerFocus = ComputePlayerFocus();
        Vector3 desiredFocus = playerFocus;

        bool wantsCursorFocus = motor && motor.CurrentStance == TopDownMotor.Stance.Active;
        float focusWeight = wantsCursorFocus ? ComputeActiveFocusWeight() : 0f;

        if (wantsCursorFocus)
        {
            bool updated = false;

            if (motor.HasCursorTarget)
            {
                Vector3 constrained = ConstrainCursorFocus(playerFocus, motor.CursorTarget, focusWeight);

                if (focusWeight > 0f)
                {
                    lastCursorFocus = constrained;
                    hasCursorFocus = true;
                    desiredFocus = constrained;
                    updated = true;
                }
            }

            if (!updated && hasCursorFocus)
            {
                if (focusWeight > 0f)
                {
                    desiredFocus = lastCursorFocus;
                    updated = true;
                }
                else
                {
                    hasCursorFocus = false;
                }
            }

            if (!updated)
            {
                desiredFocus = playerFocus;
            }
        }
        else
        {
            hasCursorFocus = false;
        }

        if (focusSmoothTime <= Mathf.Epsilon)
        {
            focusPosition = desiredFocus;
            focusVelocity = Vector3.zero;
        }
        else
        {
            if (wantsCursorFocus != wasActiveFocus)
            {
                focusVelocity = Vector3.zero;
            }

            focusPosition = Vector3.SmoothDamp(focusPosition, desiredFocus, ref focusVelocity, focusSmoothTime);
        }

        wasActiveFocus = wantsCursorFocus;
    }

    Vector3 ConstrainCursorFocus(Vector3 playerFocus, Vector3 cursorWorld, float focusWeight)
    {
        Vector3 cursorFocus = cursorWorld;
        if (targetOffset.sqrMagnitude > 0f)
        {
            cursorFocus += new Vector3(targetOffset.x, 0f, targetOffset.z);
        }

        cursorFocus.y = playerFocus.y;

        Vector3 displacement = cursorFocus - playerFocus;
        displacement.y = 0f;

        float planarDistance = displacement.magnitude;

        if (planarDistance > 0.0001f)
        {
            float maxDistance = Mathf.Max(0f, maxActiveFocusDistance);
            if (planarDistance > maxDistance && maxDistance > 0f)
            {
                displacement = displacement * (maxDistance / planarDistance);
            }
        }

        return playerFocus + displacement * Mathf.Clamp01(focusWeight);
    }

    Vector3 ComputePlayerFocus()
    {
        if (!target)
        {
            return transform.position;
        }

        return target.position + targetOffset;
    }

    void ApplyOrbit()
    {
        Vector3 focus = GetFocusPosition();
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -distance);
        transform.position = focus + offset;
        transform.rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);
    }

    Vector3 GetFocusPosition()
    {
        return focusPosition;
    }

    float ComputeActiveFocusWeight()
    {
        if (!orbitCamera)
        {
            orbitCamera = GetComponent<Camera>();
        }

        Vector2 pointerPosition;
        if (!TryGetPointerPosition(out pointerPosition) || orbitCamera == null)
        {
            return 1f;
        }

        Vector3 viewport = orbitCamera.ScreenToViewportPoint(new Vector3(pointerPosition.x, pointerPosition.y, 0f));
        Vector2 offsetFromCenter = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f) * 2f;
        float radius = Mathf.Clamp01(offsetFromCenter.magnitude);

        if (radius <= activeFocusCenterRadius)
        {
            return 0f;
        }

        float span = Mathf.Max(0.0001f, 1f - activeFocusCenterRadius);
        float normalized = Mathf.Clamp01((radius - activeFocusCenterRadius) / span);
        return Mathf.Pow(normalized, activeFocusEdgeResponse);
    }

    static bool TryGetPointerPosition(out Vector2 pointerPosition)
    {
        var mouse = Mouse.current;
        if (mouse != null)
        {
            pointerPosition = mouse.position.ReadValue();
            return true;
        }

        pointerPosition = Vector2.zero;
        return false;
    }

    void WarpCursorToLock(Mouse mouse)
    {
        mouse.WarpCursorPosition(orbitCursorLockPosition);
        InputState.Change(mouse.position, orbitCursorLockPosition);
    }
}
