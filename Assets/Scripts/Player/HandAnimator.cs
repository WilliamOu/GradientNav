using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class HandAnimator : MonoBehaviour
{
    public InputActionProperty triggerInput;
    public InputActionProperty gripInput;

    public Animator animator;

    // Update is called once per frame
    void Update()
    {
        float triggerValue = triggerInput.action.ReadValue<float>();
        float gripValue = gripInput.action.ReadValue<float>();

        animator.SetFloat("Trigger", triggerValue);
        animator.SetFloat("Grip", gripValue);
    }
}