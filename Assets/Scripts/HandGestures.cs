using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandGestures : MonoBehaviour
{
    [SerializeField]
    private OVRHand ovrHand;

    [SerializeField]
    private OVRHand.Hand handType = OVRHand.Hand.None;

    private void OnEnable()
    {
        if(ovrHand == null)
        {
            Logger.Instance.LogInfo("ovrHand must be set in the inspector...");
        }
        else
        {
            Logger.Instance.LogInfo("ovrHand was set correctly in the inspector...");
        }
    }

    private void Update()
    {
        if(ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Index))
        {
            Logger.Instance.LogInfo($"Hand {handType} Pinch Strength ({ovrHand.GetFingerPinchStrength(OVRHand.HandFinger.Index)})");
        }
    }
}
