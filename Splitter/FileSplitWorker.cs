﻿/*
    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using FileSplitter.Attributes;
using FileSplitter.Exceptions;
using FileSplitter.Enums;
using System;
using System.IO;
using System.Text;

namespace FileSplitter
{
    /// <summary>
    /// File Splitter class.
    /// This class does all the calculations and the splitting operations
    /// </summary>
    /// <remarks>
    /// Renamed class to prevent conflicts and prevent having to use global::
    /// </remarks>
    internal class FileSplitWorker
    {
        /// <summary>
        /// Default buffer size
        /// </summary>
        static readonly Int32 BUFFER_SIZE = (Int32)UnitAttribute.GetFromField<SplitUnit>(SplitUnit.KiloBytes).CalculatedFactor * 4;
        /// <summary>
        /// Buffer size for big files
        /// </summary>
        static readonly Int32 BUFFER_SIZE_BIG = (Int32)UnitAttribute.GetFromField<SplitUnit>(SplitUnit.MegaBytes).CalculatedFactor * 10;
        /// <summary>
        /// 1 MB constant
        /// </summary>
        static readonly Int64 MEGABYTE = (Int64)UnitAttribute.GetFromField<SplitUnit>(SplitUnit.MegaBytes).CalculatedFactor;// 1048576L;
        /// <summary>
        /// 1 GB constant
        /// </summary>
        static readonly Int64 GIGABYTE = (Int64)UnitAttribute.GetFromField<SplitUnit>(SplitUnit.GigaBytes).CalculatedFactor;// 1073741824L;
        /// <summary>
        /// The minimum part size we allow
        /// </summary>
        static readonly Int32 MINIMUM_PART_SIZE = BUFFER_SIZE;
        #region File System related limits
        const string DriveFormat_FAT12 = "FAT12";
        const string DriveFormat_FAT16 = "FAT16";
        const string DriveFormat_FAT32 = "FAT32";
        const Int32 DriveFormat_FAT12_MaxAmount = 32;
        const Int32 DriveFormat_FAT16_MaxAmount = 2;
        const Int32 DriveFormat_FAT32_MaxAmount = 4;
        static readonly Int64 DriveFormat_FAT12_Factor = MEGABYTE;
        static readonly Int64 DriveFormat_FAT16_Factor = GIGABYTE;
        static readonly Int64 DriveFormat_FAT32_Factor = GIGABYTE;
        const string DriveFormat_FAT12_FactorName = "Mb";
        const string DriveFormat_FAT16_FactorName = "Gb";
        const string DriveFormat_FAT32_FactorName = "Gb";
        #endregion
        /// <summary>
        /// Delegate for Split start
        /// </summary>
        public delegate void StartHandler();
        /// <summary>
        /// Delegate for Split end
        /// </summary>
        public delegate void FinishHandler();
        /// <summary>
        /// Delegate for Split process
        /// </summary>
        /// <param name="sender">splitter</param>
        /// <param name="args">process parameters</param>
        public delegate void ProcessHandler(Object sender, ProcessingArgs args);

        /// <summary>
        /// Delegate for Split messages
        /// </summary>
        /// <param name="server"></param>
        /// <param name="args"></param>
        public delegate void MessageHandler(Object server, MessageArgs args);

        /// <summary>
        /// Spliter Start event
        /// </summary>
        public event StartHandler start;

        /// <summary>
        /// Splitern End event
        /// </summary>
        public event FinishHandler finish;

        /// <summary>
        /// Splitter process event
        /// </summary>
        public event ProcessHandler processing;

        /// <summary>
        /// Splitter messages event
        /// </summary>
        public event MessageHandler message;

        /// <summary>
        /// Getter for the part size in bytes
        /// </summary>
        public Int64 PartSize { get; set; }

        /// <summary>
        /// Filename to be split
        /// </summary>
        public String FileName { get; set; }

        /// <summary>
        /// Operation Mode
        /// </summary>
        public SplitUnit OperationMode { get; set; }

        /// <summary>
        /// Calculates number of parts, based on size of file a part size
        /// </summary>
        // modified to return long
        // relies on other code to ensure file name exists
        public Int32 Parts
        {
            get
            {
                Int32 parts = 0;
                if (OperationMode != SplitUnit.Lines)
                {
                    if (this.FileName != null && this.FileName.Length > 0 && File.Exists(this.FileName))
                    {
                        FileInfo fi = new FileInfo(this.FileName);
                        if (fi.Length > this.PartSize)
                        {
                            parts = (Int32)Math.Ceiling((double)fi.Length / this.PartSize);
                        }
                        else {
                            parts = 1;
                        }
                    }
                }
                return parts;
            }
        }

        /// <summary>
        /// File pattern. If different to default pattern
        /// {0} for current file number
        /// {1} for total files
        /// </summary>
        public String FileFormatPattern { get; set; }

        /// <summary>
        /// Delete original file if end is correct
        /// </summary>
        public Boolean DeleteOriginalFile { get; set; }

        /// <summary>
        /// Destination folder. If different to current folder
        /// </summary>
        public String DestinationFolder { get; set; }

        /// <summary>
        /// File used to store generated file names
        /// </summary>
        public String GenerationLogFile { get; set; }

        /// <summary>
        /// Adds the file name to the log file
        /// </summary>
        /// <param name="fileName"></param>
        private void registerCreatedFile(String fileName)
        {
            if (GenerationLogFile != null)
            {
                File.AppendAllText(GenerationLogFile, fileName + Environment.NewLine);
            }
        }

        /// <summary>
        /// Launch splitStart event
        /// </summary>
        private void onStart()
        {
            if (start != null)
            {
                start();
            }
        }

        /// <summary>
        /// Launch splitEnd event
        /// </summary>
        private void onFinish()
        {
            if (finish != null)
            {
                finish();
            }
        }

        /// <summary>
        /// Launch splitProcess event
        /// </summary>
        /// <param name="filename">actual processing filename</param>
        /// <param name="part">actual part</param>
        /// <param name="partSizeWritten">bytes written in this part</param>
        /// <param name="totalParts">total parts</param>
        /// <param name="partSize">part size</param>
        private void onProcessing(String filename, Int64 part, Int64 partSizeWritten, Int64 totalParts, Int64 partSize)
        {
            if (processing != null)
            {
                processing(this, new ProcessingArgs(filename, part, partSizeWritten, totalParts, partSize));
            }
        }

        /// <summary>
        /// Launch Split Message event
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="type"></param>
        private void onMessage(ExceptionsMessages msg, params Object[] parameters)
        {
            if (message != null)
            {
                message(this, new MessageArgs(msg, parameters));
            }
        }

        /// <summary>
        /// Generates and registers next file name in the correct destination folder
        /// </summary>
        /// <param name="actualFileNumber"></param>
        /// <returns></returns>
        private String getNextFileName(Int64 actualFileNumber)
        {
            String actualFileName = String.Format(FileFormatPattern, actualFileNumber, this.Parts);
            registerCreatedFile(actualFileName);
            if (DestinationFolder != null)
            {
                actualFileName = Path.Combine(DestinationFolder, actualFileName);
            }
            return actualFileName;
        }

        /// <summary>
        /// Split file by number of lines
        /// </summary>
        /// <param name="inputFileName"></param>
        /// <param name="fileNameInfo"></param>
        /// <param name="sourceFileSize"></param>
        private void splitByLines(String inputFileName, Int64 sourceFileSize)
        {

            // File Pattern
            Int64 actualFileNumber = 1;
            String actualFileName = getNextFileName(actualFileNumber);

            // Error if cant create new file
            StreamReader inputReader = new StreamReader(inputFileName, true);
            Encoding enc = inputReader.CurrentEncoding;
            StreamWriter outputWriter = new StreamWriter(actualFileName, false, enc, BUFFER_SIZE_BIG);

            Int32 linesReaded = 0;
            String line = "";
            do
            {
                line = inputReader.ReadLine();
                if (line != null)
                {
                    linesReaded++;
                    outputWriter.WriteLine(line);
                    if (linesReaded >= this.PartSize)
                    {
                        linesReaded = 0;
                        outputWriter.Flush();
                        outputWriter.Close();
                        actualFileNumber++;
                        actualFileName = getNextFileName(actualFileNumber);
                        outputWriter = new StreamWriter(actualFileName, false, enc, BUFFER_SIZE_BIG);
                    }
                }
                onProcessing(actualFileName, actualFileNumber, linesReaded, 0, this.PartSize);
            } while (line != null);
            outputWriter.Flush();
            outputWriter.Close();
            inputReader.Close();
        }

        /// <summary>
        /// Split by size
        /// </summary>
        private void splitBySize(String inputFileName, Int64 sourceFileSize)
        {
            // Minimum Part Size allowed 4kb
            if (this.PartSize < MINIMUM_PART_SIZE)
            {
                onMessage(ExceptionsMessages.ERROR_MINIMUN_PART_SIZE, MINIMUM_PART_SIZE);
                throw new SplitFailedException();
            }

            // Prepare file buffer
            int bufferSize = BUFFER_SIZE_BIG;
            if (bufferSize > this.PartSize)
            {
                bufferSize = Convert.ToInt32(this.PartSize);
            }
            byte[] buffer = new byte[bufferSize];
            Int64 bytesInTotal = 0;

            // File Pattern
            Int64 actualFileNumber = 1;
            String actualFileName = getNextFileName(actualFileNumber);

            // Check if file can be opened for read
            FileStream stmOriginal = null;
            FileStream stmWriter = null;
            try
            {
                stmOriginal = File.OpenRead(this.FileName);
            }
            catch
            {
                onMessage(ExceptionsMessages.ERROR_OPENING_FILE);
                throw new SplitFailedException();
            }

            // Error if cant create new file
            try
            {
                stmWriter = File.Create(actualFileName);
            }
            catch
            {
                onMessage(ExceptionsMessages.ERROR_OPENING_FILE); //TODO new error message 
                throw new SplitFailedException();
            }

            Int64 parts = this.Parts;
            Int64 bytesInPart = 0;
            Int32 bytesInBuffer = 1;
            while (bytesInBuffer > 0)
            {    // keep going while there is unprocessed data left in the input buffer

                // Read the file to current file pointer to fill buffer from 0 to total length
                bytesInBuffer = stmOriginal.Read(buffer, 0, buffer.Length);

                // If contains data process the buffer readed
                if (bytesInBuffer > 0)
                {

                    // The entire block can be written into the same file
                    if ((bytesInPart + bytesInBuffer) <= this.PartSize)
                    {
                        stmWriter.Write(buffer, 0, bytesInBuffer);
                        bytesInPart += bytesInBuffer;
                        // Finish the current file and start a new file if required
                    }
                    else {

                        // Fill the current file to the Full size if has pending content
                        Int32 pendingToWrite = (Int32)(this.PartSize - bytesInPart);

                        // Write the pending content to the current file
                        // If 0 The size written in last iteration is equals to block size
                        if (pendingToWrite > 0)
                        {
                            stmWriter.Write(buffer, 0, pendingToWrite);
                        }
                        stmWriter.Flush();
                        stmWriter.Close();

                        // If the last write does not fullfill all the content, continue
                        if ((bytesInTotal + pendingToWrite) < sourceFileSize)
                        {
                            bytesInPart = 0;

                            actualFileNumber++;
                            actualFileName = getNextFileName(actualFileNumber);
                            stmWriter = File.Create(actualFileName);

                            // Write the rest of the buffer if required into the new file
                            // if pendingToWrite is more than 0 write the part not written in previous file
                            // else write all in the new file
                            if (pendingToWrite > 0 && pendingToWrite <= bytesInBuffer)
                            {
                                //stmWriter.Write(buffer,bytesInBuffer - pendingToWrite, bytesInBuffer);
                                stmWriter.Write(buffer, pendingToWrite, (bytesInBuffer - pendingToWrite));
                                bytesInPart += (bytesInBuffer - pendingToWrite);
                            }
                            else if (pendingToWrite == 0)
                            {
                                stmWriter.Write(buffer, 0, bytesInBuffer);
                                bytesInPart += bytesInBuffer;
                            }
                        }
                    }
                    bytesInTotal += bytesInBuffer;
                    onProcessing(actualFileName, actualFileNumber, bytesInPart, parts, this.PartSize);
                    // If no more data in source file close last stream
                }
                else {
                    stmWriter.Flush();
                    stmWriter.Close();
                }
            }
            if (bytesInTotal != sourceFileSize)
            {
                onMessage(ExceptionsMessages.ERROR_TOTALSIZE_NOTEQUALS);
                throw new SplitFailedException();
            }
        }
        /// <summary>
        /// Do split operation
        /// </summary>
        public void doSplit()
        {
            try
            {
                onStart();

                FileInfo fileNameInfo = new FileInfo(this.FileName);

                // Check Space available
                DriveInfo driveInfo = new DriveInfo(fileNameInfo.Directory.Root.Name);
                Int64 sourceFileSize = fileNameInfo.Length;

                // Builds default pattern if FileFormatPattern is null
                if (FileFormatPattern == null)
                {
                    // Use the part's string length (e.g. '123' -> 3) to determine the amount of padding needed
                    String zeros = new String('0', this.Parts.ToString().Length); // Padding
                    FileFormatPattern = Path.GetFileNameWithoutExtension(this.FileName) + "_{0:" + zeros + "}({1:" + zeros + "})" + fileNameInfo.Extension;
                }

                // Exception if not space available
                if (driveInfo.AvailableFreeSpace <= sourceFileSize)
                {
                    onMessage(ExceptionsMessages.ERROR_NO_SPACE_TO_SPLIT);
                    throw new SplitFailedException();
                }

                // Check Drive Format Limitations
                if (driveInfo.DriveFormat == DriveFormat_FAT16)
                { // 2gb
                    if (this.PartSize > DriveFormat_FAT16_MaxAmount * DriveFormat_FAT16_Factor)
                    {
                        onMessage(ExceptionsMessages.ERROR_FILESYSTEM_NOTALLOW_SIZE, DriveFormat_FAT16, DriveFormat_FAT16_MaxAmount, DriveFormat_FAT16_FactorName);
                        throw new SplitFailedException();
                    }
                }
                else if (driveInfo.DriveFormat == DriveFormat_FAT32)
                {  // 4gb
                    if (this.PartSize > DriveFormat_FAT32_MaxAmount * DriveFormat_FAT32_Factor)
                    {
                        onMessage(ExceptionsMessages.ERROR_FILESYSTEM_NOTALLOW_SIZE, DriveFormat_FAT32, DriveFormat_FAT32_MaxAmount, DriveFormat_FAT32_FactorName);
                        throw new SplitFailedException();
                    }
                }
                else if (driveInfo.DriveFormat == DriveFormat_FAT12)
                {  // 4gb
                    if (this.PartSize > DriveFormat_FAT12_MaxAmount * DriveFormat_FAT12_Factor)
                    {
                        onMessage(ExceptionsMessages.ERROR_FILESYSTEM_NOTALLOW_SIZE, DriveFormat_FAT12, DriveFormat_FAT12_MaxAmount, DriveFormat_FAT12_FactorName);
                        throw new SplitFailedException();
                    }
                }

                // Try create destination
                if (DestinationFolder != null)
                {
                    DirectoryInfo di = new DirectoryInfo(DestinationFolder);
                    if (!di.Exists)
                    {
                        di.Create();
                    }
                }

                if (OperationMode != SplitUnit.Lines)
                {
                    splitBySize(this.FileName, sourceFileSize);
                }
                else {
                    splitByLines(this.FileName, sourceFileSize);
                }

                // If no Exception breaks copy (delete new if required)
                if (DeleteOriginalFile && !fileNameInfo.IsReadOnly)
                {
                    fileNameInfo.Delete();
                }
            }
            catch (Exception)
            {
                //TODO
            }
            finally
            {
                onFinish();
            }
        }
    }
}