using System.Reflection;
using UnityEngine;

public class ULipSyncProfileRouter : MonoBehaviour
{
    public Object maleProfile;
    public Object femaleProfile;

    [Tooltip("Lascia vuoto per auto-find sullo stesso GO")]
    public Component uLipSync;
    public Component uLipSyncAudioSource;

    void Awake()
    {
        if (!uLipSync) uLipSync = GetComponent<Component>(); // fallback (userai Assign in inspector meglio)
    }

    public void ApplyGender(string gender)
    {
        var isFemale = !string.IsNullOrEmpty(gender) && gender.ToLowerInvariant().Contains("female");
        var profile = isFemale ? femaleProfile : maleProfile;
        if (!profile) return;

        if (!uLipSync) uLipSync = GetComponent("uLipSync");
        if (!uLipSyncAudioSource) uLipSyncAudioSource = GetComponent("uLipSyncAudioSource");

        SetProfile(uLipSync, profile);
        SetProfile(uLipSyncAudioSource, profile);

        Debug.Log($"[uLipSync] Profile applicato: {(isFemale ? "Female" : "Male")}");
    }

    private static void SetProfile(Component comp, Object profile)
    {
        if (!comp || !profile) return;

        var t = comp.GetType();

        // property: Profile / profile
        var p = t.GetProperty("Profile", BindingFlags.Public | BindingFlags.Instance)
             ?? t.GetProperty("profile", BindingFlags.Public | BindingFlags.Instance);

        if (p != null && p.CanWrite && p.PropertyType.IsAssignableFrom(profile.GetType()))
        {
            p.SetValue(comp, profile);
            return;
        }

        // field: profile / m_profile
        var f = t.GetField("profile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
             ?? t.GetField("m_profile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (f != null && f.FieldType.IsAssignableFrom(profile.GetType()))
        {
            f.SetValue(comp, profile);
        }
    }
}
