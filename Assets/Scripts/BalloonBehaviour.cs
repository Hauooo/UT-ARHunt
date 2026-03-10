using System.Collections;
using UnityEngine;

/// <summary>
/// Attached to each balloon prefab instance spawned by ARBalloonPopManager.
/// Handles bobbing animation, slow Y-rotation, and pop behaviour.
/// </summary>
public class BalloonBehaviour : MonoBehaviour
{
    [SerializeField] private float bobSpeed  = 1.5f;
    [SerializeField] private float bobHeight = 0.08f;
    [SerializeField] private float rotateSpeed = 30f;   // degrees per second

    /// <summary>Fired once when this balloon is successfully popped.</summary>
    public event System.Action OnPopped;

    private Vector3 _spawnPosition;
    private bool _isPopped;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Try to put this object on the "Balloon" layer so Physics.Raycast can filter it.
        int layer = LayerMask.NameToLayer("Balloon");
        if (layer >= 0)
            gameObject.layer = layer;
        else
            Debug.LogWarning("[BalloonBehaviour] 'Balloon' layer not found. " +
                             "Please add it in Edit → Project Settings → Tags & Layers.");
    }

    private void Start()
    {
        _spawnPosition = transform.position;
    }

    private void Update()
    {
        if (_isPopped) return;

        // Bob up and down around the spawn position
        Vector3 pos = _spawnPosition;
        pos.y += Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = pos;

        // Slowly rotate around the Y axis
        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
    }

    // ── Pop ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Call this when the player taps the balloon.
    /// Guards against double-tap; fires <see cref="OnPopped"/>, plays a
    /// scale-up/down animation, then destroys the GameObject.
    /// </summary>
    public void Pop()
    {
        if (_isPopped) return;
        _isPopped = true;

        OnPopped?.Invoke();
        StartCoroutine(PopAnimation());
    }

    private IEnumerator PopAnimation()
    {
        // Scale up
        Vector3 originalScale = transform.localScale;
        Vector3 bigScale       = originalScale * 1.4f;

        float elapsed  = 0f;
        float duration = 0.12f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(originalScale, bigScale, elapsed / duration);
            yield return null;
        }

        // Scale down to nothing
        elapsed  = 0f;
        duration = 0.1f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(bigScale, Vector3.zero, elapsed / duration);
            yield return null;
        }

        Destroy(gameObject);
    }
}
