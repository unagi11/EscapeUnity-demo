namespace Escape.Storage
{
    // 플랫폼 저장 실패 여부까지 처리하는 단일 저장소를 제공한다.
    public static class SaveDataStore
    {
        private static readonly ISaveDataStore Store = new PlatformSaveDataStore();

        public static ISaveDataStore Current => Store;
    }
}
