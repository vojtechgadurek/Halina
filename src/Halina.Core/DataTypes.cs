namespace Halina.Core;

interface IHashMetadataData<THash, TMetaData, TData>
{
    TMetaData MetaData { get; set;}
    THash Hash { get; set;}
    TData Data { get; set;}
}


