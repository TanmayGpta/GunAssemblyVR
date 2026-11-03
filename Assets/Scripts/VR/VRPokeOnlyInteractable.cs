using UnityEngine.XR.Interaction.Toolkit;

namespace VRArmory
{
    // A simple interactable only a fingertip poke can use. Grab interactors ignore it, so it never steals a grab from
    // a zone next to it (the safety lever sits right beside the slide's rack grip).
    public class VRPokeOnlyInteractable : XRSimpleInteractable
    {
        public override bool IsHoverableBy(IXRHoverInteractor interactor) => interactor is XRPokeInteractor && base.IsHoverableBy(interactor);

        public override bool IsSelectableBy(IXRSelectInteractor interactor) => interactor is XRPokeInteractor && base.IsSelectableBy(interactor);
    }
}
