using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Basic owner-only movement controller for a networked player.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class NetworkPlayerController : NetworkBehaviour
{
    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField] private float _rotationSpeed = 360f;

    private CharacterController _characterController;

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        enabled = IsOwner;
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;

        Vector3 move = new(input.x, 0f, input.y);
        if (move.sqrMagnitude > 1f)
            move.Normalize();

        Vector3 worldMove = transform.TransformDirection(move) * _moveSpeed;
        _characterController.SimpleMove(worldMove);

        if (move.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(transform.TransformDirection(move), Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _rotationSpeed * Time.deltaTime);
        }
    }
}
