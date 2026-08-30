namespace Escape.Storage
{
    // 세이브 UI가 실제 저장 위치를 알지 않고 문자열 데이터를 읽고 쓰게 하는 경계다.
    public interface ISaveDataStore
    {
        bool Write(string key, string value);
        bool TryRead(string key, out string value);
        bool Delete(string key);
    }
}
