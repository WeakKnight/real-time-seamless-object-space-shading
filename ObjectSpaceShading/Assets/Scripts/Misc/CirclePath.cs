using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

public class CirclePath : MonoBehaviour
{
    public float radius = 1.0f;
    public float spd = 1.0f;

    private Vector3 center;
    private float t;
    // Start is called before the first frame update
    void Start()
    {
        center = transform.position;
        t = 0.0f;
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = center + new Vector3(radius * Mathf.Sin(t), radius * Mathf.Cos(t), 0.0f);
        t += spd * Time.deltaTime;
    }
}
