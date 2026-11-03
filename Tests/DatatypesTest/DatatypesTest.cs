/* Copyright (C) 2007  The Trustees of Indiana University
 *
 * Use, modification and distribution is subject to the Boost Software
 * License, Version 1.0. (See accompanying file LICENSE_1_0.txt or copy at
 * http://www.boost.org/LICENSE_1_0.txt)
 *  
 * Authors: Douglas Gregor
 *          Andrew Lumsdaine
 * 
 * This test exercises MPI.NET's ability to construct and use MPI datatypes 
 * for .NET value types using reflection.
 */
using System;
using MPI;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using MPI.TestCommons;

unsafe public struct Dimensions
{
    public fixed float values[11];
}

public struct Secretive
{
    public Secretive(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
     
    private int x;
    public int y;

    public static bool operator ==(Secretive s1, Secretive s2)
    {
        return s1.x == s2.x && s1.y == s2.y;
    }

    public static bool operator !=(Secretive s1, Secretive s2)
    {
        return !(s1 == s2);
    }

    public override bool Equals(object other)
    {
        if (other is Secretive)
        {
            return this == (Secretive)other;
        }
        throw new ArgumentException("other object is not Secretive");
    }

    public override int GetHashCode()
    {
        return x ^ y;
    }

    public override string ToString()
    {
        return x.ToString() + ":" + y.ToString();
    }
}

[Serializable]
class AggregateData
{
    public AggregateData(int NumBytes)
    {
        if (NumBytes >= 0)
        {
            Name = "Aggregate data";
            KeyValuePairs = new Dictionary<string, string>();
            KeyValuePairs.Add("Key1", "Value1");
            KeyValuePairs.Add("Key2", "Value2");
            KeyValuePairs.Add("Key3", "Value3");
            BinaryData = new byte[NumBytes];
            for (int i = 0; i < NumBytes; ++i)
                BinaryData[i] = (byte)(i % 256);
        }
    }

    public bool Check(int BytesExpected)
    {
        if (!Name.Equals("Aggregate data"))
            return false;
        if (!KeyValuePairs["Key1"].Equals("Value1"))
            return false;
        if (!KeyValuePairs["Key2"].Equals("Value2"))
            return false;
        if (!KeyValuePairs["Key3"].Equals("Value3"))
            return false;
        if (BinaryData.Length != BytesExpected)
            return false;
        for (int i = 0; i < BinaryData.Length; ++i)
            if (BinaryData[i] != i % 256)
                return false;
        return true;
    }

    private string Name;
    private Dictionary<string, string> KeyValuePairs;
    private byte[] BinaryData;
}

[Serializable]
struct ContainsBool
{
    int int1;
    char char1;
    bool bool1;
    char char2;

    public ContainsBool(int value)
    {
        int1 = value;
        char1 = value.ToString()[0];
        bool1 = value >= 10;
        char2 = bool1 ? value.ToString()[1] : 'X';
    }

    public static bool operator ==(ContainsBool x, ContainsBool y)
    {
        return x.int1 == y.int1 && x.char1 == y.char1
            && x.bool1 == y.bool1 && x.char2 == y.char2;
    }

    public static bool operator !=(ContainsBool x, ContainsBool y)
    {
        return !(x == y);
    }

    public override bool Equals(object other)
    {
        if (other is ContainsBool)
        {
            return this == (ContainsBool)other;
        }
        throw new ArgumentException("other object is not a ContainsBool");
    }

    public override int GetHashCode()
    {
        return int1;
    }

    public override string ToString()
    {
        return int1.ToString() + ':' + char1.ToString() + ':' 
            + bool1.ToString() + ':' + char2.ToString();
    }

}

class DatatypesTest
{
    [Serializable]
    class SerializationNode
    {
        public SerializationNode Next;
        public static bool CallbackInvoked;

        [OnDeserialized]
        void OnDeserialized(StreamingContext context)
        {
            CallbackInvoked = true;
        }
    }

    static void ExpectException<TException>(Action action) where TException : Exception
    {
        bool caught = false;
        try
        {
            action();
        }
        catch (TException)
        {
            caught = true;
        }
        MPIDebug.Assert(caught, "Expected " + typeof(TException).Name);
    }

    public static void VerifySerialization()
    {
        ISerializer serializer = new MPI.Serialization().Serializer;
        using (var stream = new MemoryStream())
        {
            serializer.Serialize(stream, new AggregateData(512));
            stream.Position = 0;
            MPIDebug.Assert(serializer.Deserialize<AggregateData>(stream).Check(512));
            MPIDebug.Assert(stream.CanRead && stream.CanWrite);
        }
        using (var stream = new MemoryStream())
        {
            serializer.Serialize(stream, new string[] { "first", null, "last" });
            serializer.Serialize(stream, "next value");
            stream.Position = 0;
            var result = serializer.Deserialize<string[]>(stream);
            MPIDebug.Assert(result.Length == 3 && result[0] == "first"
                && result[1] == null && result[2] == "last");
            MPIDebug.Assert(serializer.Deserialize<string>(stream) == "next value");
        }

        var knownSerializer = new MPI.DataContractSerializer(new[] { typeof(SerializationNode) });
        var node = new SerializationNode();
        node.Next = node;
        using (var stream = new MemoryStream())
        {
            knownSerializer.Serialize<object>(stream, node);
            stream.Position = 0;
            SerializationNode.CallbackInvoked = false;
            ExpectException<SerializationException>(() => serializer.Deserialize<object>(stream));
            MPIDebug.Assert(!SerializationNode.CallbackInvoked);
            stream.Position = 0;
            var result = (SerializationNode)knownSerializer.Deserialize<object>(stream);
            MPIDebug.Assert(ReferenceEquals(result, result.Next) && SerializationNode.CallbackInvoked);
        }
        using (var stream = new MemoryStream())
        {
            serializer.Serialize(stream, new string('x', 1024));
            stream.Position = 0;
            var limited = new MPI.DataContractSerializer(readerQuotas:
                new XmlDictionaryReaderQuotas { MaxStringContentLength = 16 });
            ExpectException<SerializationException>(() => limited.Deserialize<string>(stream));
        }
        using (var stream = new MemoryStream())
        {
            serializer.Serialize(stream, new[] { "a", "b", "c", "d" });
            stream.Position = 0;
            var limited = new MPI.DataContractSerializer(maxItemsInObjectGraph: 2);
            ExpectException<SerializationException>(() => limited.Deserialize<string[]>(stream));
        }
        // A benign BinaryFormatter string payload must not be accepted by the new default.
        using (var stream = new MemoryStream(Convert.FromBase64String("AAEAAAD/////AQAAAAAAAAAGAQAAAAZsZWdhY3kL")))
            ExpectException<SerializationException>(() => serializer.Deserialize<string>(stream));
#pragma warning disable 618
        ExpectException<NotSupportedException>(() => BinaryFormatterSerializer.Default.Deserialize<object>(Stream.Null));
        ExpectException<NotSupportedException>(() => BinaryFormatterSerializer.Default.Serialize(Stream.Null, "legacy"));
#pragma warning restore 618
    }

    static void VerifyReceiveLimits(Intracommunicator comm)
    {
        int bufferSize = comm.Serialization.BufferSize;
        long messageLimit = comm.Serialization.MaxSerializedMessageSize;
        comm.Serialization.BufferSize = 64;
        comm.Serialization.MaxSerializedMessageSize = 64;
        try
        {
            if (comm.Rank == 0)
            {
                comm.Send(-1, 1, 100);
                comm.Send(65, 1, 101);
                using (var stream = new MemoryStream())
                {
                    comm.Serialization.Serializer.Serialize(stream, new string('x', 1024));
                    var firstBlock = new byte[64];
                    Array.Copy(stream.ToArray(), firstBlock, firstBlock.Length);
                    comm.Send(64, 1, 102);
                    comm.Send(firstBlock, 1, 102);
                    comm.Send(64, 1, 102);
                }
            }
            else
            {
                for (int tag = 100; tag <= 102; tag++)
                {
                    int receiveTag = tag;
                    ExpectException<SerializationException>(() =>
                    {
                        string value;
                        CompletedStatus status;
                        comm.Receive(0, receiveTag, out value, out status);
                    });
                }
            }
            comm.Barrier();
        }
        finally
        {
            comm.Serialization.BufferSize = bufferSize;
            comm.Serialization.MaxSerializedMessageSize = messageLimit;
        }
    }

    static void VerifyStreamedMessages(Intracommunicator comm)
    {
        string expected = new string('x', 1024);
        using (var stream = new MemoryStream())
        {
            comm.Serialization.Serializer.Serialize(stream, expected);
            foreach (int blockSize in new[] { 64, (int)stream.Length })
            {
                using (var testComm = (Intracommunicator)comm.Clone())
                {
                    testComm.Serialization.BufferSize = blockSize;
                    if (testComm.Rank == 0)
                    {
                        testComm.Send(expected, 1, 103);
                        testComm.Send(expected, 1, 103);
                    }
                    else
                    {
                        string value;
                        CompletedStatus status;
                        testComm.Receive(0, 103, out value, out status);
                        MPIDebug.Assert(value == expected);
                        MPI.Serialization.ReceiveLarge(testComm, 0, 103, out value);
                        MPIDebug.Assert(value == expected);
                    }
                    testComm.Barrier();
                }
            }
        }
    }

    private struct Hidden
    {
        public Hidden(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        private int x;
        public int y;

        public static bool operator ==(Hidden s1, Hidden s2)
        {
            return s1.x == s2.x && s1.y == s2.y;
        }

        public static bool operator !=(Hidden s1, Hidden s2)
        {
            return !(s1 == s2);
        }

        public override bool Equals(object other)
        {
            if (other is Hidden)
            {
                return this == (Hidden)other;
            }
            throw new ArgumentException("other object is not Hidden");
        }

        public override int GetHashCode()
        {
            return x ^ y;
        }

        public override string ToString()
        {
            return x.ToString() + ":" + y.ToString();
        }
    }

    static int Main(string[] args)
    {
        return MPIDebug.Execute(DoTest, args);
    }

    public static void DoTest(string[] args)
    {
        VerifySerialization();
        int dataSize = 10000000;
        //using (MPI.Environment env = new MPI.Environment(ref args))
        {
            if (Communicator.world.Size != 2)
            {
                System.Console.WriteLine("The Datatypes test must be run with two processes.");
                System.Console.WriteLine("Try: mpiexec -np 2 datatypes.exe");
            }
            else
            {
                VerifyReceiveLimits(Communicator.world);
                VerifyStreamedMessages(Communicator.world);
                Communicator.world.Serialization.BufferSize = 1024 * 1024;
            }
            if (Communicator.world.Size == 2 && Communicator.world.Rank == 0)
            {
                // Send an object that contains a "fixed" field
                Dimensions dims;
                unsafe
                {
                    for (int i = 0; i < 11; ++i)
                        dims.values[i] = (float)i;
                }
                Communicator.world.Send(dims, 1, 0);

                // Send an object that contains non-public fields
                Secretive secret = new Secretive(17, 25);
                Communicator.world.Send(secret, 1, 1);

                // Send an object with complex data
                AggregateData aggregate = new AggregateData(dataSize);
                Communicator.world.Send(aggregate, 1, 2);

                // Send an object with a private type
                Hidden hidden = new Hidden(17, 25);
                Communicator.world.Send(hidden, 1, 3);

                // Send a struct that requires serialization.
                ContainsBool containsBool = new ContainsBool(17);
                Communicator.world.Send(containsBool, 1, 4);
            }
            else if (Communicator.world.Size == 2)
            {
                // Receive and check an object that contains a "fixed" field
                Dimensions dims;
                Communicator.world.Receive(0, 0, out dims);
                unsafe
                {
                    for (int i = 0; i < 11; ++i)
                    {
                        System.Console.WriteLine(dims.values[i].ToString() + " ");
                        MPIDebug.Assert(dims.values[i] == (float)i);
                    }
                }

                // Receive and check an object that contains non-public fields
                Secretive secret;
                Communicator.world.Receive(0, 1, out secret);
                System.Console.WriteLine(secret);
                MPIDebug.Assert(secret == new Secretive(17, 25));

                // Receive and check the "complex data"
                AggregateData aggregate = Communicator.world.Receive<AggregateData>(0, 2);
                if (!aggregate.Check(dataSize))
                {
                    System.Console.Error.WriteLine("Error: complex data not properly transmitted");
                    MPI.Environment.Abort(1);
                }

                // Receive and check an object with a private type
                Hidden hidden;
                Communicator.world.Receive(0, 3, out hidden);
                System.Console.WriteLine(hidden);
                MPIDebug.Assert(hidden == new Hidden(17, 25));

                // Receive and check a struct that requires serialization
                ContainsBool containsBool;
                Communicator.world.Receive(0, 4, out containsBool);
                System.Console.WriteLine(containsBool);
                MPIDebug.Assert(containsBool == new ContainsBool(17));
            }

        }
    }
}
