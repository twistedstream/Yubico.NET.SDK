// Copyright 2021 Yubico AB
//
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using Yubico.Core.Iso7816;
using Yubico.PlatformInterop;

namespace Yubico.Core.Devices.SmartCard
{
    public class DesktopSmartCardConnection : ISmartCardConnection
    {
        private readonly SCardContext _context;
        private readonly SCardCardHandle _cardHandle;
        private SCARD_PROTOCOL _activeProtocol;

        private class TransactionScope : IDisposable
        {
            private readonly DesktopSmartCardConnection _thisConnection;
            private bool _disposedValue;

            public TransactionScope(DesktopSmartCardConnection thisConnection)
            {
                _thisConnection = thisConnection;
            }

            protected virtual void Dispose(bool disposing)
            {
                if (!_disposedValue)
                {
                    _ = PlatformLibrary.Instance.SCard.EndTransaction(
                        _thisConnection._cardHandle,
                        SCARD_DISPOSITION.LEAVE_CARD);

                    _disposedValue = true;
                }
            }

            ~TransactionScope()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: false);
            }

            public void Dispose()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }

        internal DesktopSmartCardConnection(
            SCardContext context,
            SCardCardHandle cardHandle,
            SCARD_PROTOCOL activeProtocol)
        {
            _context = context;
            _cardHandle = cardHandle;
            _activeProtocol = activeProtocol;
        }

        /// <summary>
        /// Begins a transacted connection to the smart card.
        /// </summary>
        /// <remarks>
        /// This method has no effect on platforms which do not support transactions.
        /// </remarks>
        /// <returns>An IDisposable that represents the transaction.</returns>
        /// <exception cref="SCardException">
        /// Thrown when the underlying platform smart card subsystem encounters an error.
        /// </exception>
        public IDisposable BeginTransaction(out bool cardWasReset)
        {
            cardWasReset = false;
            uint result = PlatformLibrary.Instance.SCard.BeginTransaction(_cardHandle);

            // Sometime the smart card is left in a state where it needs to be reset prior to beginning
            // a transaction. We should automatically handle this case.
            if (result == ErrorCode.SCARD_W_RESET_CARD)
            {
                Reconnect();

                result = PlatformLibrary.Instance.SCard.BeginTransaction(_cardHandle);
                cardWasReset = true;
            }

            if (result != ErrorCode.SCARD_S_SUCCESS)
            {
                throw new SCardException(ExceptionMessages.SCardTransactionFailed, result);
            }

            return new TransactionScope(this);
        }

        /// <summary>
        /// Synchronously transmit a command APDU to the smart card.
        /// </summary>
        /// <param name="commandApdu">A command to send to the smart card.</param>
        /// <returns>A response APDU containing the smart card's reply.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="commandApdu"/> is null.
        /// </exception>
        /// <exception cref="SCardException">
        /// Thrown when the underlying platform smart card subsystem encounters an error.
        /// </exception>
        public ResponseApdu Transmit(CommandApdu commandApdu)
        {
            if (commandApdu is null)
            {
                throw new ArgumentNullException(nameof(commandApdu));
            }

            // The YubiKey likely will never return a buffer larger than 512 bytes without instead
            // using response chaining.
            byte[] outputBuffer = new byte[512];

            uint result = PlatformLibrary.Instance.SCard.Transmit(
                _cardHandle,
                new SCARD_IO_REQUEST(_activeProtocol),
                commandApdu.AsByteArray(),
                IntPtr.Zero,
                outputBuffer,
                out int outputBufferSize
                );

            if (result != ErrorCode.SCARD_S_SUCCESS)
            {
                throw new SCardException(ExceptionMessages.SCardTransmitFailure, result);
            }

            Array.Resize(ref outputBuffer, outputBufferSize);

            return new ResponseApdu(outputBuffer);
        }

        public void Reconnect()
        {
            uint result = PlatformLibrary.Instance.SCard.Reconnect(
                _cardHandle,
                SCARD_SHARE.SHARED,
                SCARD_PROTOCOL.T1,
                SCARD_DISPOSITION.RESET_CARD,
                out SCARD_PROTOCOL updatedActiveProtocol);

            if (result != ErrorCode.SCARD_S_SUCCESS)
            {
                throw new SCardException(ExceptionMessages.SCardReconnectFailed, result);
            }

            _activeProtocol = updatedActiveProtocol;
        }

        #region IDisposable Support
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _cardHandle.Dispose();
                _context.Dispose();
            }

            _disposed = true;
        }
        #endregion
    }
}