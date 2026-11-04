using System.Runtime.InteropServices;

namespace FlipRace.Web
{
    public static class TrackFileBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR && !FLIP_MANAGED_CHECK
        [DllImport("__Internal")] static extern void FlipCatalogState(string phase, string message);
        [DllImport("__Internal")] static extern void FlipTrackImportResult(int success, string message);
#endif
        public static void CatalogState(string phase, string message)
        {
#if UNITY_WEBGL && !UNITY_EDITOR && !FLIP_MANAGED_CHECK
            FlipCatalogState(phase, message);
#endif
        }
        public static void ImportResult(bool success, string message)
        {
#if UNITY_WEBGL && !UNITY_EDITOR && !FLIP_MANAGED_CHECK
            FlipTrackImportResult(success ? 1 : 0, message);
#endif
        }
    }
}
