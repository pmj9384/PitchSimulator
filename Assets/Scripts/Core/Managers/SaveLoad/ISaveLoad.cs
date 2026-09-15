
//로컬이면 로컬에서, 파이어베이스면 파이어베이스에서 받아오는 형태로 추후 수정할 예정
using System;

public enum DataSourceType
{
    Local,
    Firebase,
}

public interface ISaveLoad
{
    public DataSourceType SaveDataSouceType
    {
        get;
    }

    public void Save();

    public void Load(); //저장 데이터가 없을때 동작할 로드

    //public void Load(Save~); //저장 데이터가 있을때 동작할 로드

}
