using System;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // A simple interactable with a custom "who may use me" rule. By default only a fingertip poke can use it; grab
    // interactors ignore it, so it never steals a grab from a zone next to it (the safety lever sits right beside the
    // slide's rack grip). Set canUse to change the rule (e.g. "poke or ray, but not the hand holding the pistol").
    public class VRPokeOnlyInteractable : XRSimpleInteractable
    {
        public Func<IXRInteractor, bool> canUse;

        bool Allowed(IXRInteractor interactor) => canUse != null ? canUse(interactor) : interactor is XRPokeInteractor;

        public override bool IsHoverableBy(IXRHoverInteractor interactor) => Allowed(interactor) && base.IsHoverableBy(interactor);

        public override bool IsSelectableBy(IXRSelectInteractor interactor) => Allowed(interactor) && base.IsSelectableBy(interactor);
    }
}
