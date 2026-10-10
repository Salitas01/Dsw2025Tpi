using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dsw2025Tpi.Application.Exceptions
{
    public abstract class ExceptionBase: Exception
    {
        protected ExceptionBase(string? code, string? message): base(message)
        {
           Code = code;
        }
        public string? Code { get; }
    }
}
