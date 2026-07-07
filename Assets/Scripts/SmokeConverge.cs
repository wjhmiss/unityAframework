using UnityEngine;

/// <summary>
/// Forces particles to converge toward the transform center, then rise upward.
/// Attach to a ParticleSystem that emits from a spherical shell.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class SmokeConverge : MonoBehaviour
{
    [SerializeField] private float inwardSpeed = 3f;
    [SerializeField] private float upwardSpeed = 2f;
    [SerializeField] private float convergeRadius = 1f;

    private ParticleSystem _ps;
    private ParticleSystem.Particle[] _particles;
    private int _lastMaxParticles;

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        _lastMaxParticles = _ps.main.maxParticles;
        _particles = new ParticleSystem.Particle[_lastMaxParticles];
    }

    void LateUpdate()
    {
        int max = _ps.main.maxParticles;
        if (max > _particles.Length)
        {
            _particles = new ParticleSystem.Particle[max];
            _lastMaxParticles = max;
        }

        int count = _ps.GetParticles(_particles);
        Vector3 center = transform.position;

        for (int i = 0; i < count; i++)
        {
            Vector3 toCenter = center - _particles[i].position;
            toCenter.y = 0f; // only converge horizontally
            float horizDist = toCenter.magnitude;

            if (horizDist > convergeRadius)
            {
                // Move toward center
                Vector3 dir = toCenter / horizDist;
                _particles[i].velocity = new Vector3(
                    dir.x * inwardSpeed,
                    upwardSpeed,
                    dir.z * inwardSpeed
                );
            }
            else
            {
                // At center: rise straight up
                _particles[i].velocity = new Vector3(0f, upwardSpeed * 1.5f, 0f);
            }
        }

        _ps.SetParticles(_particles, count);
    }
}
