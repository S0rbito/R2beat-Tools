using UnityEngine;

public class SlowAnimation : MonoBehaviour
{
    public float speed = 0.3f;

    void Start()
    {
        Animator anim = GetComponent<Animator>();
        if (anim == null) return;
        anim.speed = speed;
    }
}