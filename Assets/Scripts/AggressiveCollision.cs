using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AggressiveCollision : MonoBehaviour
{
    [Header("Knockback Settings")]
    [Tooltip("Multiplier for additional knockback force. Higher values push the defender farther.")]
    public float forceMultiplier = 5.0f; 
    
    [Tooltip("Minimum speed advantage required to count as the attacking side.")]
    public float speedThreshold = 1.0f;  

    private Rigidbody rb;
    // Key idea: store the true velocity right before a collision happens.
    public Vector3 velocityBeforePhysicsUpdate { get; private set; }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // Record current velocity every physics step so we can access pre-collision speed.
        velocityBeforePhysicsUpdate = rb.velocity;
    }

    void OnCollisionEnter(Collision collision)
    {
        // Ensure this collision is with another player.
        // Make sure your spheres use the "Player" tag in Unity.
        if (collision.gameObject.CompareTag("Player"))
        {
            Rigidbody otherRb = collision.gameObject.GetComponent<Rigidbody>();
            AggressiveCollision otherScript = collision.gameObject.GetComponent<AggressiveCollision>();

            if (otherRb != null && otherScript != null)
            {
                // Compare both sides' pre-collision speeds.
                float mySpeed = velocityBeforePhysicsUpdate.magnitude;
                float otherSpeed = otherScript.velocityBeforePhysicsUpdate.magnitude;

                // If my speed is sufficiently higher, treat me as the attacker.
                if (mySpeed > otherSpeed + speedThreshold)
                {
                    // 1) Compute knockback direction (from me to the opponent).
                    Vector3 pushDir = (collision.transform.position - transform.position);
                    pushDir.y = 0; // Keep this on the ground plane to avoid unintended vertical launch.
                    pushDir.Normalize();

                    // 2) Estimate transferred impact using speed difference.
                    float speedDifference = mySpeed - otherSpeed;
                    
                    // Optionally try Mathf.Pow(speedDifference, 2) for a stronger nonlinear knockback.
                    float extraForce = speedDifference * forceMultiplier;

                    // 3) Apply an impulse force to the defender.
                    otherRb.AddForce(pushDir * extraForce, ForceMode.Impulse);
                    
                    // Optional: play a heavy-hit VFX/SFX here.
                    // Debug.Log("Heavy hit triggered! Extra force: " + extraForce);
                }
            }
        }
    }
}