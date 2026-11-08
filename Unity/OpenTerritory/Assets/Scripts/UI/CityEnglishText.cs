using UnityEngine.UI;

namespace OpenTerritory
{
    // All runtime labels share this presentation boundary, including dynamic captions.
    public sealed class CityEnglishText : Text
    {
        string lastInput;
        public override string text
        {
            get => base.text;
            set
            {
                if (lastInput == value && base.text == CityEnglish.Text(value)) return;
                lastInput = value; base.text = CityEnglish.Text(value);
            }
        }
    }
}
