
namespace Dsw2025Tpi.Application.Exceptions
{
    public class DuplicatedEntityException : ExceptionBase
    {
        public DuplicatedEntityException(string code, string message) : base(code, message)
        {
        }
    }
}
