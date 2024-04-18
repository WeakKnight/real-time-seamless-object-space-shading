using UnityEngine;
using UnityEngine.Playables;

[RequireComponent(typeof(PlayableDirector))]
public class TimelineScale : MonoBehaviour
{
    private PlayableDirector director;
    public float playbackSpeed = 1.0f;

    void Start()
    {
        director = GetComponent<PlayableDirector>();
        if (director != null)
        {
            director.timeUpdateMode = DirectorUpdateMode.Manual;

            director.Play();
        }
    }

    void Update()
    {
        if (director != null)
        {
            director.time += Time.deltaTime * playbackSpeed;
        }
    }
}
