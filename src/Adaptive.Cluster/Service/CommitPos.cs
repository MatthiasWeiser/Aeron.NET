﻿using System;
using Adaptive.Aeron;
using Adaptive.Agrona;
using Adaptive.Agrona.Concurrent.Status;

namespace Adaptive.Cluster.Service
{
    /// <summary>
    /// Counter representing the commit position that can consumed by a state machine on a stream for the current
    /// leadership term.
    ///
    /// Key layout as follows:
    ///
    ///   0                   1                   2                   3
    ///   0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    ///  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///  |             Recording ID for the leadership term              |
    ///  |                                                               |
    ///  +---------------------------------------------------------------+
    ///  |            og Position at base of leadership term             |
    ///  |                                                               |
    ///  +---------------------------------------------------------------+
    ///  |                     Leadership Term ID                        |
    ///  |                                                               |
    ///  +---------------------------------------------------------------+
    ///  |                        Log Session ID                         |
    ///  +---------------------------------------------------------------+
    ///
    /// </summary>
    public class CommitPos
    {
        /// <summary>
        /// Type id of a consensus position counter.
        /// </summary>
        public const int COMMIT_POSITION_TYPE_ID = 203;

        /// <summary>
        /// Represents a null value if the counter is not found.
        /// </summary>
        public const int NULL_VALUE = -1;

        /// <summary>
        /// Human readable name for the counter.
        /// </summary>
        public const string NAME = "commit-pos: leadershipTermId=";

        public const int RECORDING_ID_OFFSET = 0;
        public static readonly int LOG_POSITION_OFFSET = RECORDING_ID_OFFSET + BitUtil.SIZE_OF_LONG;
        public static readonly int LEADERSHIP_TERM_ID_OFFSET = LOG_POSITION_OFFSET + BitUtil.SIZE_OF_LONG;
        public static readonly int SESSION_ID_OFFSET = LEADERSHIP_TERM_ID_OFFSET + BitUtil.SIZE_OF_LONG;
        public static readonly int KEY_LENGTH = SESSION_ID_OFFSET + BitUtil.SIZE_OF_INT;

        /// <summary>
        /// Allocate a counter to represent the commit position on stream for the current leadership term.
        /// </summary>
        /// <param name="aeron">            to allocate the counter. </param>
        /// <param name="tempBuffer">       to use for building the key and label without allocation. </param>
        /// <param name="recordingId">      for the current term. </param>
        /// <param name="logPosition">      of the log at the beginning of the leadership term. </param>
        /// <param name="leadershipTermId"> of the log at the beginning of the leadership term. </param>
        /// <param name="sessionId">        of the active log for the current leadership term. </param>
        /// <returns> the <seealso cref="Counter"/> for the consensus position. </returns>
        public static Counter Allocate(Aeron.Aeron aeron, IMutableDirectBuffer tempBuffer, long recordingId, long logPosition, long leadershipTermId, int sessionId)
        {
            tempBuffer.PutLong(RECORDING_ID_OFFSET, recordingId);
            tempBuffer.PutLong(LOG_POSITION_OFFSET, logPosition);
            tempBuffer.PutLong(LEADERSHIP_TERM_ID_OFFSET, leadershipTermId);
            tempBuffer.PutInt(SESSION_ID_OFFSET, sessionId);

            int labelOffset = 0;
            labelOffset += tempBuffer.PutStringWithoutLengthAscii(KEY_LENGTH + labelOffset, NAME);
            labelOffset += tempBuffer.PutLongAscii(KEY_LENGTH + labelOffset, leadershipTermId);
            labelOffset += tempBuffer.PutStringWithoutLengthAscii(KEY_LENGTH + labelOffset, " logSessionId=");
            labelOffset += tempBuffer.PutIntAscii(KEY_LENGTH + labelOffset, sessionId);

            return aeron.AddCounter(COMMIT_POSITION_TYPE_ID, tempBuffer, 0, KEY_LENGTH, tempBuffer, KEY_LENGTH, labelOffset);
        }

        /// <summary>
        /// Find the active counter id for a stream based on the session id.
        /// </summary>
        /// <param name="counters">  to search within. </param>
        /// <param name="sessionId"> for the active log. </param>
        /// <returns> the counter id if found otherwise <seealso cref="CountersReader.NULL_COUNTER_ID"/>. </returns>
        public static int FindCounterIdBySession(CountersReader counters, int sessionId)
        {
            IDirectBuffer buffer = counters.MetaDataBuffer;

            for (int i = 0, size = counters.MaxCounterId; i < size; i++)
            {
                if (counters.GetCounterState(i) == CountersReader.RECORD_ALLOCATED)
                {
                    int recordOffset = CountersReader.MetaDataOffset(i);

                    if (buffer.GetInt(recordOffset + CountersReader.TYPE_ID_OFFSET) == COMMIT_POSITION_TYPE_ID && 
                        buffer.GetInt(recordOffset + CountersReader.KEY_OFFSET + SESSION_ID_OFFSET) == sessionId)
                    {
                        return i;
                    }
                }
            }

            return CountersReader.NULL_COUNTER_ID;
        }

        /// <summary>
        /// Get the recording id for the current leadership term.
        /// </summary>
        /// <param name="counters">  to search within. </param>
        /// <param name="counterId"> for the active commit position. </param>
        /// <returns> the recording id if found otherwise <seealso cref="NULL_VALUE"/>. </returns>
        public static long GetRecordingId(CountersReader counters, int counterId)
        {
            IDirectBuffer buffer = counters.MetaDataBuffer;

            if (counters.GetCounterState(counterId) == CountersReader.RECORD_ALLOCATED)
            {
                int recordOffset = CountersReader.MetaDataOffset(counterId);

                if (buffer.GetInt(recordOffset + CountersReader.TYPE_ID_OFFSET) == COMMIT_POSITION_TYPE_ID)
                {
                    return buffer.GetLong(recordOffset + CountersReader.KEY_OFFSET + RECORDING_ID_OFFSET);
                }
            }

            return NULL_VALUE;
        }

        /// <summary>
        /// Get the accumulated log position as a base for this leadership term.
        /// </summary>
        /// <param name="counters">  to search within. </param>
        /// <param name="counterId"> for the active commit position. </param>
        /// <returns> the base log position if found otherwise <seealso cref="NULL_VALUE"/>. </returns>
        public static long GetBaseLogPosition(CountersReader counters, int counterId)
        {
            IDirectBuffer buffer = counters.MetaDataBuffer;

            if (counters.GetCounterState(counterId) == CountersReader.RECORD_ALLOCATED)
            {
                int recordOffset = CountersReader.MetaDataOffset(counterId);

                if (buffer.GetInt(recordOffset + CountersReader.TYPE_ID_OFFSET) == COMMIT_POSITION_TYPE_ID)
                {
                    return buffer.GetLong(recordOffset + CountersReader.KEY_OFFSET + LOG_POSITION_OFFSET);
                }
            }

            return NULL_VALUE;
        }

        /// <summary>
        /// Get the leadership term id for the given commit position.
        /// </summary>
        /// <param name="counters">  to search within. </param>
        /// <param name="counterId"> for the active commit position. </param>
        /// <returns> the leadership term id if found otherwise <seealso cref="NULL_VALUE"/>. </returns>
        public static long GetLeadershipTermId(CountersReader counters, int counterId)
        {
            IDirectBuffer buffer = counters.MetaDataBuffer;

            if (counters.GetCounterState(counterId) == CountersReader.RECORD_ALLOCATED)
            {
                int recordOffset = CountersReader.MetaDataOffset(counterId);

                if (buffer.GetInt(recordOffset + CountersReader.TYPE_ID_OFFSET) == COMMIT_POSITION_TYPE_ID)
                {
                    return buffer.GetLong(recordOffset + CountersReader.KEY_OFFSET + LEADERSHIP_TERM_ID_OFFSET);
                }
            }

            return NULL_VALUE;
        }

        /// <summary>
        /// Get the session id for a active counter. Since a session id can have any value there is no possible
        /// null value so an exception will be thrown if the counter is not found.
        /// </summary>
        /// <param name="counters">  to search within. </param>
        /// <param name="counterId"> for the active commit position. </param>
        /// <returns> the session id if found other which throw <seealso cref="InvalidOperationException"/> </returns>
        /// <exception cref="InvalidOperationException"> if counter is not found. </exception>
        public static int GetLogSessionId(CountersReader counters, int counterId)
        {
            IDirectBuffer buffer = counters.MetaDataBuffer;

            if (counters.GetCounterState(counterId) == CountersReader.RECORD_ALLOCATED)
            {
                int recordOffset = CountersReader.MetaDataOffset(counterId);

                if (buffer.GetInt(recordOffset + CountersReader.TYPE_ID_OFFSET) == COMMIT_POSITION_TYPE_ID)
                {
                    return buffer.GetInt(recordOffset + CountersReader.KEY_OFFSET + SESSION_ID_OFFSET);
                }
            }

            throw new InvalidOperationException("No active counter for id: " + counterId);
        }

        /// <summary>
        /// Is the counter still active and log still recording?
        /// </summary>
        /// <param name="counters">    to search within. </param>
        /// <param name="counterId">   to search for. </param>
        /// <returns> true if the counter is still active otherwise false. </returns>
        public static bool IsActive(CountersReader counters, int counterId)
        {
            IDirectBuffer buffer = counters.MetaDataBuffer;

            if (counters.GetCounterState(counterId) == CountersReader.RECORD_ALLOCATED)
            {
                int recordOffset = CountersReader.MetaDataOffset(counterId);

                return buffer.GetInt(recordOffset + CountersReader.TYPE_ID_OFFSET) == COMMIT_POSITION_TYPE_ID;
            }

            return false;
        }
    }
}