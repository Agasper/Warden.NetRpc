namespace Warden.Rpc
{
    public interface ICustomMessage
    {
        void WriteTo(WriteFormatterInfo writeFormatterInfo);
        void MergeFrom(ReadFormatterInfo readFormatterInfo);
    }
}
