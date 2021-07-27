namespace Warden.Rpc
{
    public interface IWardenMessage
    {
        void WriteTo(WriteFormatterInfo writeFormatterInfo);
        void MergeFrom(ReadFormatterInfo readFormatterInfo);
    }
}
