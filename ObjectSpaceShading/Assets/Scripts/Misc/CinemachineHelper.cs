using Cinemachine;
using UnityEngine;

[RequireComponent(typeof(CinemachineBrain))]
public class CinemachineHelper : MonoBehaviour
{
    void Start()
    {
        GetComponent<CinemachineBrain>().enabled = true;
        GetComponent<RenderStats>().enabled = true;
    }
}
