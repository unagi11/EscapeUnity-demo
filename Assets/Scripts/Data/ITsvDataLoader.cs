using System.Collections.Generic;

namespace Escape.Data
{
    // TSV 파일을 특정 데이터 타입 목록으로 불러오기 위한 공통 인터페이스.
    public interface ITsvDataLoader<T> where T : class, new()
    {
        // 타입 이름을 기준으로 기본 Resources 경로를 찾아 TSV를 불러온다.
        IReadOnlyList<T> Load();

        // 지정한 Resources 경로 또는 Assets/Resources 경로에서 TSV를 불러온다.
        IReadOnlyList<T> Load(string resourcePath);

        // 기본 경로에서 TSV를 불러온 뒤 id 검색이 가능한 테이블로 감싼다.
        TsvTable<T> LoadTable();

        // 지정한 경로에서 TSV를 불러온 뒤 id 검색이 가능한 테이블로 감싼다.
        TsvTable<T> LoadTable(string resourcePath);
    }
}
