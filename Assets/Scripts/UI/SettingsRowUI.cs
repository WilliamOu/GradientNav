using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class SettingRowUI : MonoBehaviour
{
    [Header("Common")]
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private TextMeshProUGUI description;

    [Header("Controls")]
    [SerializeField] private Toggle toggleInput;
    [SerializeField] private TMP_InputField numberInput;
    [SerializeField] private TMP_Dropdown dropdownInput;

    public void Setup(SettingDef data)
    {
        label.text = data.Name;
        description.text = data.Description;

        toggleInput.gameObject.SetActive(false);
        numberInput.gameObject.SetActive(false);
        dropdownInput.gameObject.SetActive(false);

        switch (data)
        {
            case BoolSetting b:
                SetupBool(b);
                break;
            case FloatSetting n:
                SetupFloat(n);
                break;
            case IntegerSetting i:
                SetupInteger(i);
                break;
            case EnumSetting e:
                SetupEnum(e);
                break;
            default:
                Debug.LogError($"Unknown setting type: {data.GetType()}");
                break;
        }
    }

    private void SetupBool(BoolSetting data)
    {
        toggleInput.gameObject.SetActive(true);
        toggleInput.isOn = data.Value;

        toggleInput.onValueChanged.RemoveAllListeners();
        toggleInput.onValueChanged.AddListener((val) =>
        {
            data.Value = val;
            data.OnChanged?.Invoke(val);
        });
    }

    private void SetupFloat(FloatSetting data)
    {
        numberInput.gameObject.SetActive(true);
        numberInput.text = data.Value.ToString("F2");

        numberInput.onEndEdit.RemoveAllListeners();

        numberInput.onEndEdit.AddListener((str) =>
        {
            if (float.TryParse(str, out float result))
            {
                float clamped = Mathf.Clamp(result, data.Min, data.Max);

                data.Value = clamped;
                data.OnChanged?.Invoke(clamped);

                if (clamped != result)
                    numberInput.text = clamped.ToString("F2");
            }
            else
            {
                numberInput.text = data.Value.ToString("F2");
            }
        });
    }

    private void SetupInteger(IntegerSetting data)
    {
        numberInput.gameObject.SetActive(true);
        numberInput.text = data.Value.ToString("F0");

        numberInput.onEndEdit.RemoveAllListeners();

        numberInput.onEndEdit.AddListener((str) =>
        {
            if (int.TryParse(str, out int result))
            {
                int clamped = Mathf.Clamp(result, data.Min, data.Max);

                data.Value = clamped;
                data.OnChanged?.Invoke(clamped);

                if (clamped != result)
                    numberInput.text = clamped.ToString("F0");
            }
            else
            {
                numberInput.text = data.Value.ToString("F0");
            }
        });
    }

    private void SetupEnum(EnumSetting data)
    {
        dropdownInput.gameObject.SetActive(true);
        dropdownInput.ClearOptions();

        dropdownInput.AddOptions(data.Options);
        dropdownInput.value = data.SelectedIndex;

        dropdownInput.onValueChanged.RemoveAllListeners();
        dropdownInput.onValueChanged.AddListener((index) =>
        {
            data.SelectedIndex = index;
            data.OnChanged?.Invoke(index);
        });
    }
}