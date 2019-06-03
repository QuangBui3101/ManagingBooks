﻿using System;
using System.Windows;
using System.Windows.Input;
using ManagingBooks.Model;
using ManagingBooks.Windows;
using Microsoft.Data.Sqlite;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.ComponentModel;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Barcodes;
using Microsoft.Win32;
using System.Diagnostics;
using System.Linq;
using WPFCustomMessageBox;
using System.Data.OleDb;
using System.Data;
using System.IO;
using System.Globalization;
using System.Windows.Controls;
using iText.Layout.Borders;
using iText.Kernel.Geom;
using System.Windows.Media;

namespace ManagingBooks
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private GridViewColumnHeader listViewSortCol = null;
        private SortAdorner listViewSortAdorner = null;

        BackgroundWorker Search = new BackgroundWorker();
        BackgroundWorker Delete = new BackgroundWorker();
        int LastIndex = -1;

        Uri English = new Uri(".\\Resources\\Resources.xaml", UriKind.Relative);
        Uri German = new Uri(".\\Resources\\Resources.de.xaml", UriKind.Relative);

        string ExportFolder = System.IO.Path.Combine(AppContext.BaseDirectory, "Barcode");
        //SearchBook DeleteBook;

        public MainWindow()
        {
            InitializeComponent();
            SearchBookModel context = new SearchBookModel();
            this.DataContext = context;
            //SearchBookModel context = this.DataContext as SearchBookModel;
            context.DisplayBooks = new ObservableCollection<SearchBook>();
            context.ListBookPrint = new ObservableCollection<SearchBook>();
            SearchList.ItemsSource = context.DisplayBooks;
            context.DisplayBooks.Clear();
            Search.WorkerReportsProgress = true;
            Search.DoWork += Search_DoWork;
            Search.ProgressChanged += Search_ProgressChanged;
            Search.RunWorkerCompleted += Search_RunWorkerCompleted;
            // start collecting books from DB
            Search.RunWorkerAsync(NumberOfBooks());

            // hide books which are not relevant to UserFilter
            CollectionView view = CollectionViewSource.GetDefaultView(SearchList.ItemsSource) as CollectionView;
            view.Filter = UserFilter;

            // *** not used ***
            Delete.WorkerReportsProgress = true;
            Delete.DoWork += Delete_DoWork;
            Delete.ProgressChanged += Delete_ProgressChanged;
            Delete.RunWorkerCompleted += Delete_RunWorkerCompleted;
            // *** not used ***

            // Clear book info
            ClearEntries(context);
            GetListViewColVisible(context);

            // Set default Search By option
            context.SearchBy = Application.Current.FindResource("MainWindow.SearchBy.Number").ToString();
        }

        /// <summary>
        /// Access DB and fetch the books
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Search_DoWork(object sender, DoWorkEventArgs e)
        {
            int max = (int)e.Argument;
            int i = 0;
            int progressPercentage;

            SqliteConnection con;
            SqlMethods.SqlConnect(out con);
            var selectCommand = con.CreateCommand();
            selectCommand.CommandText = "SELECT b.BookId,b.Number,b.Title,b.Version,b.Medium,a.AuthorId,a.Name,s.Signature,b.Publisher,b.Place,b.Year,b.DayBought,b.Pages,b.Price " +
                "FROM Books b " +
                "LEFT JOIN Books_Authors ba ON (b.BookId = ba.BookId) " +
                "LEFT JOIN Authors a ON (ba.AuthorId = a.AuthorId) " +
                "LEFT JOIN Books_Signatures bs ON (bs.BookId = b.BookId) " +
                "LEFT JOIN Signatures s ON (bs.SignatureId = s.SignatureId) ORDER BY b.BookId,ba.Priority,bs.Priority";
            SqliteDataReader r = selectCommand.ExecuteReader();
            int lastBookId = -1;
            int lastAuthorId = -1;
            bool finishedBook = false;
            SearchBook tempBook = new SearchBook();

            // read  DB and forming a book object
            while (r.Read())
            {
                int result;
                int.TryParse(Convert.ToString(r["BookId"]), out result);
                if (result == lastBookId)
                {
                    int result1;
                    int.TryParse(Convert.ToString(r["AuthorId"]), out result1);
                    if (result1 == lastAuthorId)
                    {
                        string temp = Convert.ToString(r["Signature"]);
                        if (!tempBook.Signatures.Contains(temp))
                        {
                            tempBook.Signatures += "-" + Convert.ToString(r["Signature"]);
                        }
                    }
                    if (result1 != lastAuthorId)
                    {
                        tempBook.Authors += ", " + Convert.ToString(r["Name"]);
                        lastAuthorId = result1;
                    }
                }
                else
                {
                    finishedBook = true;
                    if (finishedBook && (!string.IsNullOrEmpty(tempBook.Title)))
                    {
                        // report progress change
                        progressPercentage = Convert.ToInt32(((double)i / max) * 100);
                        (sender as BackgroundWorker).ReportProgress(progressPercentage, tempBook);

                        finishedBook = false;
                    }
                    tempBook = new SearchBook();
                    tempBook.BookId = result;
                    lastBookId = result;
                    //int.TryParse(Convert.ToString(r["Number"]), out result);
                    tempBook.Number = Convert.ToString(r["Number"]);
                    tempBook.Title = Convert.ToString(r["Title"]);
                    tempBook.Publishers = Convert.ToString(r["Publisher"]);
                    int.TryParse(Convert.ToString(r["Year"]), out result);
                    tempBook.Year = result;
                    int.TryParse(Convert.ToString(r["Version"]), out result);
                    tempBook.Version = result;
                    tempBook.Medium = Convert.ToString(r["Medium"]);
                    tempBook.Place = Convert.ToString(r["Place"]);
                    tempBook.Date = Convert.ToString(r["DayBought"]);
                    int.TryParse(Convert.ToString(r["Pages"]), out result);
                    tempBook.Pages = result;
                    double.TryParse(Convert.ToString(r["Price"]), out double result_d);
                    tempBook.Price = result_d;
                    int.TryParse(Convert.ToString(r["AuthorId"]), out result);
                    lastAuthorId = result;
                    tempBook.Authors = Convert.ToString(r["Name"]);
                    tempBook.Signatures = Convert.ToString(r["Signature"]);
                    i++;
                }
                Thread.Sleep(TimeSpan.FromTicks(500));
            }
            if (i != 0)
            {
                progressPercentage = Convert.ToInt32(((double)i / max) * 100);
                (sender as BackgroundWorker).ReportProgress(progressPercentage, tempBook);
            }
            e.Result = i;
            con.Close();

        }

        /// <summary>
        /// Provide progress change of fetching books
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void Search_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            context.Progress = e.ProgressPercentage;
            context.Status = "Running";
            //context.Status = Application.Current.FindResource("MainWindow.CodeBehind.Status.Running").ToString();
            if (e.UserState != null)
            {
                context.DisplayBooks.Add(e.UserState as SearchBook);
            }
        }

        // What to be done after finishing fetching books
        void Search_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            // set the status to finished
            context.Status = "Search Complete";
            //context.Status = Application.Current.FindResource("MainWindow.CodeBehind.Status.Completed").ToString();
            // focus to the previous chosen book if any
            if (LastIndex != -1)
            {
                SearchList.SelectedIndex = LastIndex;
                LastIndex = -1;
                SearchList.ScrollIntoView(SearchList.SelectedItem);
            }
        }

        /// <summary>
        /// Define the requirement to display books when searching
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private bool UserFilter(object item)
        {
            var context = this.DataContext as SearchBookModel;
            if (string.IsNullOrWhiteSpace(context.SearchText))
            {
                return true;
            }
            else
            {
                if (context.SearchBy.Equals(Application.Current.FindResource("MainWindow.SearchBy.Number")))
                    return (item as SearchBook).Number.ToString().IndexOf(context.SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
                else if (context.SearchBy.Equals(Application.Current.FindResource("MainWindow.SearchBy.Signatures")))
                    return (item as SearchBook).Signatures.StartsWith(context.SearchText, StringComparison.OrdinalIgnoreCase);
                else if (context.SearchBy.Equals(Application.Current.FindResource("MainWindow.SearchBy.Title")))
                    return (item as SearchBook).Title.IndexOf(context.SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
                else if (context.SearchBy.Equals(Application.Current.FindResource("MainWindow.SearchBy.Authors")))
                    return (item as SearchBook).Authors.IndexOf(context.SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
                else if (context.SearchBy.Equals(Application.Current.FindResource("MainWindow.SearchBy.Place")))
                    return (item as SearchBook).Place.IndexOf(context.SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
                else if (context.SearchBy.Equals(Application.Current.FindResource("MainWindow.SearchBy.Medium")))
                    return (item as SearchBook).Medium.StartsWith(context.SearchText, StringComparison.OrdinalIgnoreCase);
                else
                    return true;
            }
        }

        /// <summary>
        /// Call the AddBook window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AddWindow_Click(object sender, RoutedEventArgs e)
        {
            // After closing the window, refresh the display books
            if (!new AddBook() { Owner = this }.ShowDialog().Value)
            {
                (DataContext as SearchBookModel).DisplayBooks.Clear();
                Search.RunWorkerAsync(NumberOfBooks());
                ClearEntries(DataContext as SearchBookModel);
            }
        }

        /// <summary>
        /// Close the program
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ExitCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ExitCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        // *** not used ***
        private void SearchCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = (!string.IsNullOrWhiteSpace(BoxSearchText.Text) && !string.IsNullOrWhiteSpace(BoxSearchBy.Text));
        }

        private void SearchCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SearchBookModel context = (this.DataContext as SearchBookModel);
            CollectionViewSource.GetDefaultView(SearchList.ItemsSource).Refresh();
            if (context.SearchBy.Equals("Author"))
            {
            }

            MessageBox.Show($"Search is executed\nSearch for: {context.SearchText}\nSearch By: {context.SearchBy}", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            context.SearchText = string.Empty;
            BoxSearchBy.SelectedItem = null;
        }
        // *** not used ***

        // *** not used ***
        private void SearchAll(SearchBookModel context)
        {
            SqliteConnection con;
            SqlMethods.SqlConnect(out con);
            var selectCommand = con.CreateCommand();
            selectCommand.CommandText = "SELECT b.BookId,b.Number,b.Title,b.Version,b.Medium,a.AuthorId,a.Name,s.Signature,b.Publisher,b.Place,b.Year,b.DayBought,b.Pages,b.Price " +
                "FROM Books b " +
                "LEFT JOIN Books_Authors ba ON (b.BookId = ba.BookId) " +
                "LEFT JOIN Authors a ON (ba.AuthorId = a.AuthorId) " +
                "LEFT JOIN Books_Signatures bs ON (b.BookId = bs.BookId) " +
                "LEFT JOIN Signatures s ON (bs.SignatureId = s.SignatureId) ORDER BY b.BookId";
            SqliteDataReader r = selectCommand.ExecuteReader();
            int lastBookId = -1;
            int lastAuthorId = -1;
            SearchBook tempBook = new SearchBook();
            while (r.Read())
            {

                int result;
                int.TryParse(Convert.ToString(r["BookId"]), out result);
                if (result == lastBookId)
                {
                    int result1;
                    int.TryParse(Convert.ToString(r["AuthorId"]), out result1);
                    if (result1 == lastAuthorId)
                    {
                        string temp = Convert.ToString(r["Signature"]);
                        if (!tempBook.Signatures.Contains(temp))
                        {
                            tempBook.Signatures += " " + Convert.ToString(r["Signature"]);
                        }
                    }
                    if (result1 != lastAuthorId)
                    {
                        tempBook.Authors += ", " + Convert.ToString(r["Name"]);
                        lastAuthorId = result1;
                    }
                }
                if (result != lastBookId)
                {
                    tempBook = new SearchBook();
                    tempBook.BookId = result;
                    lastBookId = result;
                    tempBook.Number = Convert.ToString(r["Number"]);
                    tempBook.Title = Convert.ToString(r["Title"]);
                    tempBook.Publishers = Convert.ToString(r["Publisher"]);
                    int.TryParse(Convert.ToString(r["Year"]), out result);
                    tempBook.Year = result;
                    int.TryParse(Convert.ToString(r["Version"]), out result);
                    tempBook.Version = result;
                    tempBook.Medium = Convert.ToString(r["Medium"]);
                    tempBook.Place = Convert.ToString(r["Place"]);
                    tempBook.Date = Convert.ToString(r["DayBought"]);
                    int.TryParse(Convert.ToString(r["Pages"]), out result);
                    tempBook.Pages = result;
                    double.TryParse(Convert.ToString(r["Price"]), out double result_d);
                    tempBook.Price = result_d;
                    int.TryParse(Convert.ToString(r["AuthorId"]), out result);
                    lastAuthorId = result;
                    tempBook.Authors = Convert.ToString(r["Name"]);
                    tempBook.Signatures = Convert.ToString(r["Signature"]);
                    context.DisplayBooks.Add(tempBook);
                }
            }
            con.Close();
        }
        // *** not used ***

        /// <summary>
        /// Refresh current displayed books to match the certain search pattern
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BoxSearchText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(SearchList.ItemsSource).Refresh();
        }

        private void EditCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = (SearchList.SelectedItem != null);
        }

        /// <summary>
        /// Call EditBook window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EditCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            int bookId = (SearchList.SelectedItem as SearchBook).BookId;
            LastIndex = SearchList.SelectedIndex;
            EditBook editWindow = new EditBook(bookId) { Owner = this };
            if (!editWindow.ShowDialog().Value)
            {
                if (editWindow.IsNew)
                {
                    LastIndex = SearchList.Items.Count;
                }
                (DataContext as SearchBookModel).DisplayBooks.Clear();
                Search.RunWorkerAsync(NumberOfBooks());
            }
        }

        /// <summary>
        /// Provide pdf file of barcodes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PrintBarcodeCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            string pdfPath;
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.FileName = "Barcode";
            dialog.Filter = "PDF (*.pdf)|*.pdf";
            dialog.InitialDirectory = AppContext.BaseDirectory;
            if (dialog.ShowDialog(this) == true)
            {
                pdfPath = dialog.FileName;
                using (PdfWriter writer = new PdfWriter(pdfPath))
                {
                    using (PdfDocument pdf = new PdfDocument(writer))
                    {
                        Document document = new Document(pdf);
                        Barcode128 barcode = new Barcode128(pdf);
                        Table table = new Table(5, false);
                        table.SetWidth(iText.Layout.Properties.UnitValue.CreatePercentValue(100));

                        //foreach (string codeNr in context.ListBookPrint)
                        //{
                        //    barcode.SetCode(codeNr);
                        //    iText.Layout.Element.Image barcodeImage = new iText.Layout.Element.Image(barcode.CreateFormXObject(pdf));
                        //    barcodeImage.SetHorizontalAlignment(iText.Layout.Properties.HorizontalAlignment.CENTER);
                        //    //barcodeImage.Scale(1.5F, 1.5F);
                        //    Cell cell = new Cell();
                        //    //cell.Add(new Paragraph("Code 128").SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER));
                        //    cell.Add(barcodeImage);
                        //    table.AddCell(cell);
                        //}
                        //document.Add(table);
                    }
                }
                // open the pdf file for reviewing and printing
                Process proc = new Process();
                proc.StartInfo.FileName = pdfPath;
                proc.StartInfo.UseShellExecute = true;
                proc.Start();
            }
        }

        private void PrintCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            string pdfPath;
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.FileName = "List Books";
            dialog.Filter = "PDF (*.pdf)|*.pdf";
            dialog.InitialDirectory = AppContext.BaseDirectory;
            if (dialog.ShowDialog(this) == true)
            {
                string pdfTitle = Application.Current.FindResource("MainWindow.PrintList.PDF.Title").ToString();
                string pdfNumberCol = Application.Current.FindResource("MainWindow.PrintList.PDF.TableHeader.Number").ToString();
                string pdfSignatureCol = Application.Current.FindResource("MainWindow.PrintList.PDF.TableHeader.Signature").ToString();
                string pdfTitleCol = Application.Current.FindResource("MainWindow.PrintList.PDF.TableHeader.BookTitle").ToString();
                pdfPath = dialog.FileName;
                using (PdfWriter writer = new PdfWriter(pdfPath))
                {
                    using (PdfDocument pdf = new PdfDocument(writer))
                    {
                        Document document = new Document(pdf);
                        Paragraph paragraph = new Paragraph(pdfTitle)
                            .SetFontSize(20.0f)
                            .SetBold()
                            .SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER);

                        float[] columnWidths = { 1, 3, 5, 10 };
                        Table table = new Table(columnWidths);
                        table.SetWidth(iText.Layout.Properties.UnitValue.CreatePercentValue(100));

                        Cell[] cells = new Cell[4];
                        for (int i = 0; i < cells.Length; i++)
                        {
                            cells[i] = new Cell();
                        }
                        Paragraph para = new Paragraph().Add("ID").SetBold();
                        cells[0].Add(para)
                            .SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT);
                        para = new Paragraph().Add(pdfNumberCol).SetBold();
                        cells[1].Add(para).SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT);
                        para = new Paragraph().Add(pdfSignatureCol).SetBold();
                        cells[2].Add(para).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER);
                        para = new Paragraph().Add(pdfTitleCol).SetBold();
                        cells[3].Add(para).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER);
                        foreach (var cell in cells)
                        {
                            table.AddHeaderCell(cell);
                        }
                        for (int i = 0; i < context.ListBookPrint.Count; i++)
                        {
                            for (int j = 0; j < cells.Length; j++)
                            {
                                cells[j] = new Cell();
                            }
                            cells[0].Add(new Paragraph((i + 1).ToString()).SetFixedLeading(15)).SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT).SetFontSize(10);
                            cells[1].Add(new Paragraph(context.ListBookPrint[i].Number).SetFixedLeading(15)).SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT).SetFontSize(10);
                            cells[2].Add(new Paragraph(context.ListBookPrint[i].Signatures).SetFixedLeading(15)).SetTextAlignment(iText.Layout.Properties.TextAlignment.LEFT).SetFontSize(10);
                            cells[3].Add(new Paragraph(context.ListBookPrint[i].Title).SetFixedLeading(15)).SetTextAlignment(iText.Layout.Properties.TextAlignment.LEFT).SetFontSize(10);
                            foreach (var cell in cells)
                            {
                                table.AddCell(cell);
                            }
                        }
                        document.Add(paragraph);
                        document.Add(table);
                    }
                }
                Process proc = new Process();
                proc.StartInfo.FileName = pdfPath;
                proc.StartInfo.UseShellExecute = true;
                proc.Start();
            }
        }

        /// <summary>
        /// Calculate the amount of books inside the DB
        /// </summary>
        /// <returns></returns>
        private int NumberOfBooks()
        {
            int numBook = 0;
            SqlMethods.SqlConnect(out SqliteConnection con);
            var selectCommand = con.CreateCommand();
            selectCommand.CommandText = "SELECT BookId FROM Books";
            SqliteDataReader r = selectCommand.ExecuteReader();
            while (r.Read())
            {
                numBook++;
            }
            con.Close();
            return numBook;
        }

        private void DeleteCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = (SearchList.SelectedItem != null);
        }

        /// <summary>
        /// Remove certain books out of the DB and synchronize to the display
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void DeleteCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            var deleteList = SearchList.SelectedItems;
            string msg = Application.Current.FindResource("MainWindow.CodeBehind.DeleteNotify.Message").ToString();
            string caption = Application.Current.FindResource("MainWindow.CodeBehind.DeleteNotify.Caption").ToString();
            MessageBoxResult result = CustomMessageBox.ShowYesNo(msg, caption, CustomMessageBoxButton.Yes, CustomMessageBoxButton.No, MessageBoxImage.Warning);
            //MessageBoxResult result = MessageBox.Show(msg, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            var max = deleteList.Count;
            var progressHandler = new Progress<int>(value =>
            {
                context.Progress = value;
            });
            IProgress<int> progress = progressHandler as IProgress<int>;
            if (result == MessageBoxResult.Yes)
            {
                SearchList.IsEnabled = false;
                BoxSearchText.IsEnabled = false;
                BtnClearBookInfo.IsEnabled = false;
                BtnAddToPrint.IsEnabled = false;
                if (SearchList.SelectedIndex != -1)
                {
                    await Task.Run(() =>
                    {
                        for (int i = deleteList.Count - 1; i >= 0; i--)
                        {
                            var tempBook = deleteList[i] as SearchBook;
                            int bookId = tempBook.BookId;
                            SqlMethods.SqlConnect(out SqliteConnection con);
                            var tr = con.BeginTransaction();
                            var deleteCommand = con.CreateCommand();
                            deleteCommand.Transaction = tr;
                            deleteCommand.CommandText = $"DELETE FROM Books_Authors WHERE BookId={bookId}";
                            deleteCommand.ExecuteNonQuery();
                            deleteCommand = con.CreateCommand();
                            deleteCommand.CommandText = $"DELETE FROM Books_Signatures WHERE BookId={bookId}";
                            deleteCommand.ExecuteNonQuery();
                            deleteCommand = con.CreateCommand();
                            deleteCommand.CommandText = $"DELETE FROM Books WHERE BookId={bookId}";
                            deleteCommand.ExecuteNonQuery();
                            tr.Commit();
                            con.Close();

                            progress.Report(Convert.ToInt32((double)(max - i) / max * 100));
                            context.Status = "Deleting";
                            //context.Status = Application.Current.FindResource("MainWindow.CodeBehind.Status.Deleting").ToString();

                            App.Current.Dispatcher.Invoke((Action)delegate
                            {
                                context.DisplayBooks.Remove(tempBook);
                            });

                            Thread.Sleep(TimeSpan.FromTicks(5));
                        }
                    });
                    context.Status = "Delete Finished";
                    //context.Status = Application.Current.FindResource("MainWindow.CodeBehind.Status.DeleteCompleted").ToString();
                }
            }
            SearchList.IsEnabled = true;
            BoxSearchText.IsEnabled = true;
            BtnClearBookInfo.IsEnabled = true;
            BtnAddToPrint.IsEnabled = true;
        }

        // *** not used ***
        private void Delete_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            context.Status = Application.Current.FindResource("MainWindow.CodeBehind.Status.DeleteCompleted").ToString();
        }

        private void Delete_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            context.DisplayBooks.Remove(e.UserState as SearchBook);
            context.Progress = e.ProgressPercentage;
            context.Status = Application.Current.FindResource("MainWindow.CodeBehind.Status.Deleting").ToString();
            ClearEntries(context);
        }
        // *** not used ***

        // *** not used ***
        private void Delete_DoWork(object sender, DoWorkEventArgs e)
        {
            {
                this.Dispatcher.Invoke(() =>
                {
                    var deleteList = SearchList.SelectedItems;
                    //var deleteList = e.Argument as List;
                    int progressPercentage;
                    if (SearchList.SelectedIndex != -1)
                    {
                        for (int i = deleteList.Count - 1; i >= 0; i--)
                        {
                            var tempBook = deleteList[i] as SearchBook;
                            int bookId = tempBook.BookId;
                            SqlMethods.SqlConnect(out SqliteConnection con);
                            var tr = con.BeginTransaction();
                            var deleteCommand = con.CreateCommand();
                            deleteCommand.Transaction = tr;
                            deleteCommand.CommandText = $"DELETE FROM Books_Authors WHERE BookId={bookId}";
                            deleteCommand.ExecuteNonQuery();
                            deleteCommand = con.CreateCommand();
                            deleteCommand.CommandText = $"DELETE FROM Books_Signatures WHERE BookId={bookId}";
                            deleteCommand.ExecuteNonQuery();
                            deleteCommand = con.CreateCommand();
                            deleteCommand.CommandText = $"DELETE FROM Books WHERE BookId={bookId}";
                            deleteCommand.ExecuteNonQuery();
                            tr.Commit();
                            con.Close();
                            Thread.Sleep(1);
                            progressPercentage = Convert.ToInt32((double)(deleteList.Count - i) / deleteList.Count * 100);
                            (sender as BackgroundWorker).ReportProgress(progressPercentage, tempBook);
                        }
                    }
                });
            }
        }
        // *** not used ***


        private void EditPublisher_Click(object sender, RoutedEventArgs e)
        {
            new EditPublisher() { Owner = this }.ShowDialog();
        }

        private void EditPlace_Click(object sender, RoutedEventArgs e)
        {
            new EditPlace() { Owner = this }.ShowDialog();
        }

        private void EditMedium_Click(object sender, RoutedEventArgs e)
        {
            new EditMedium() { Owner = this }.ShowDialog();
        }

        /// <summary>
        /// Display information of the selected book
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            SearchBook viewBook = SearchList.SelectedItem as SearchBook;
            if (viewBook != null)
            {
                context.ViewNumber = viewBook.Number.ToString();
                context.ViewSignatures = viewBook.Signatures;
                context.ViewTitle = viewBook.Title;
                context.ViewAuthors = viewBook.Authors;
                context.ViewPublisher = viewBook.Publishers;
                context.ViewYear = viewBook.Year.ToString();
                context.ViewVersion = viewBook.Version.ToString();
                context.ViewMedium = viewBook.Medium;
                context.ViewPlace = viewBook.Place;
                context.ViewDate = viewBook.Date;
                context.ViewPages = viewBook.Pages.ToString();
                context.ViewPrice = viewBook.Price.ToString();
            }
        }

        private void ClearEntries(SearchBookModel context)
        {
            context.ViewNumber = string.Empty;
            context.ViewSignatures = string.Empty;
            context.ViewTitle = string.Empty;
            context.ViewAuthors = string.Empty;
            context.ViewPublisher = string.Empty;
            context.ViewYear = string.Empty;
            context.ViewVersion = string.Empty;
            context.ViewMedium = string.Empty;
            context.ViewPlace = string.Empty;
            context.ViewDate = string.Empty;
            context.ViewPages = string.Empty;
            context.ViewPrice = string.Empty;
        }

        private void ClearBookInfoCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SearchList.SelectedIndex = -1;
            ClearEntries(this.DataContext as SearchBookModel);
        }

        private void EnglishCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !Application.Current.Resources.MergedDictionaries[0].Source.ToString().Equals(English.OriginalString);
        }

        /// <summary>
        /// Change the laguage to English
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EnglishCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ResourceDictionary dict = new ResourceDictionary();
            dict.Source = English;
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);
            File.WriteAllText(SplashScreen.ResourcePath, string.Empty);
            using (StreamWriter w = new StreamWriter(SplashScreen.ResourcePath))
            {
                w.Write("0");
            }
        }

        private void GermanCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !Application.Current.Resources.MergedDictionaries[0].Source.ToString().Equals(German.OriginalString);
        }

        /// <summary>
        ///  Change the laguage to German
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GermanCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ResourceDictionary dict = new ResourceDictionary();
            dict.Source = German;
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);
            File.WriteAllText(SplashScreen.ResourcePath, string.Empty);
            using (StreamWriter w = new StreamWriter(SplashScreen.ResourcePath))
            {
                w.Write("1");
            }
        }

        private void PrintCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ListPrint.Items.Count != 0;
        }

        private void RemoveFromPrintCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ListPrint.SelectedItem != null;
        }

        private void RemoveFromPrint_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            SearchBook bookToRemove = ListPrint.SelectedItem as SearchBook;
            context.ListBookPrint.Remove(bookToRemove);
        }

        private void ClearPrintListCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            context.ListBookPrint.Clear();
        }

        private async void AddToPrintCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            List<SearchBook> addList = SearchList.SelectedItems.Cast<SearchBook>().ToList();
            addList.Sort((x, y) => x.BookId.CompareTo(y.BookId));
            await Task.Run(() =>
            {
                foreach (var book in addList)
                {
                    if (!context.ListBookPrint.Contains(book))
                    {
                        App.Current.Dispatcher.Invoke((Action)delegate
                        {
                            context.ListBookPrint.Add(book);
                        });
                        Thread.Sleep(TimeSpan.FromTicks(5));
                    }
                }
            });
        }

        /// <summary>
        /// Migrate the AccessDB to SqliteDB - Only works in .NET Framework
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void BtnImportMDB_Click(object sender, RoutedEventArgs e)
        {
            DateTimeFormatInfo dtfi = CultureInfo.CreateSpecificCulture("fr-FR").DateTimeFormat;

            var myDataTable = new DataTable();
            string mdbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Data\\ProNoskoDatenbank_160717.mdb");
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.InitialDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Data");
            dialog.Filter = "Microsoft Access databases (*.mdb)|*.mdb";

            if (dialog.ShowDialog(this) == true)
            {
                mdbPath = dialog.FileName;
                using (var conection = new OleDbConnection($"Provider=Microsoft.JET.OLEDB.4.0;data source={mdbPath};"))
                {
                    SqlMethods.SqlConnect(out SqliteConnection con);
                    SqliteTransaction tr = con.BeginTransaction();

                    conection.Open();
                    OleDbDataReader reader = null;

                    //OleDbCommand command = new OleDbCommand("SELECT books.Id,books.Nr,books.Signatur,books.Autor,books.Autor2,books.Autor3,books.Titel,verlag.Name,books.Auflage,books.Jahr,books.Medium,books.Standort,books.Einkauf,books.Seiten,books.Preis FROM books INNER JOIN verlag ON books.Verlag = verlag.id WHERE books.Id=33", conection);
                    OleDbCommand command = new OleDbCommand("SELECT books.Id,books.Nr,books.Signatur,books.Autor,books.Autor2,books.Autor3,books.Titel,verlag.Name,books.Auflage,books.Jahr,books.Medium,books.Standort,books.Einkauf,books.Seiten,books.Preis FROM books INNER JOIN verlag ON books.Verlag = verlag.id ORDER BY books.Id asc", conection);
                    reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        Book book = new Book();
                        int temp;
                        int.TryParse(reader["Nr"].ToString(), out temp);
                        book.Number = temp;
                        string text = reader["Signatur"].ToString();
                        string[] textArray = text.Split('-');
                        book.NoSignature = textArray.Length;
                        book.Signatures = new string[book.NoSignature];
                        for (int i = 0; i < book.NoSignature; i++)
                        {
                            if (string.IsNullOrEmpty(textArray[i]))
                            {
                                book.Signatures[i] = "N/A";
                            }
                            else
                            {
                                book.Signatures[i] = textArray[i].Trim();
                            }
                        }
                        if (!string.IsNullOrEmpty(reader["Autor3"].ToString()))
                        {
                            book.NoAuthor = 3;
                        }
                        else if (!string.IsNullOrEmpty(reader["Autor2"].ToString()))
                        {
                            book.NoAuthor = 2;
                        }
                        else
                        {
                            book.NoAuthor = 1;
                        }
                        book.Authors = new Author[book.NoAuthor];
                        for (int i = 0; i < book.Authors.Length; i++)
                        {
                            book.Authors[i] = new Author();
                        }
                        if (book.NoAuthor > 0)
                        {
                            book.Authors[0].Name = reader["Autor"].ToString().Trim();
                        }
                        if (book.NoAuthor > 1)
                        {
                            book.Authors[1].Name = reader["Autor2"].ToString().Trim();
                        }
                        if (book.NoAuthor > 2)
                        {
                            book.Authors[2].Name = reader["Autor3"].ToString().Trim();
                        }
                        book.Title = reader["Titel"].ToString().Trim();
                        book.Publisher = reader["Name"].ToString().Trim();
                        int.TryParse(reader["Auflage"].ToString(), out temp);
                        book.Version = temp;
                        int.TryParse(reader["Jahr"].ToString(), out temp);
                        book.Year = temp;
                        book.Medium = reader["Medium"].ToString();
                        if (!string.IsNullOrEmpty(reader["Standort"].ToString()))
                        {
                            book.Place = reader["Standort"].ToString().Trim();
                        }
                        else
                        {
                            book.Place = "N/A";
                        }
                        int.TryParse(reader["Seiten"].ToString(), out temp);
                        book.Pages = temp;
                        double dec;
                        double.TryParse(reader["Preis"].ToString().Replace(',', '.'), out dec);
                        book.Price = dec;
                        int.TryParse(reader["Einkauf"].ToString(), out temp);
                        if (temp == 0)
                        {
                            book.DayBought = new DateTime(1970, 1, 1).ToString("d", dtfi);
                        }
                        else
                        {
                            int year = temp % 10000;
                            temp /= 10000;
                            int month = temp % 100;
                            temp /= 100;
                            int day = temp;
                            book.DayBought = new DateTime(year, month, day).ToString("d", dtfi);
                        }

                        AddBook.AddBookToDatabase(ref con, ref tr, book);
                    }
                    tr.Commit();
                    con.Close();
                }
            }
            Search.RunWorkerAsync(NumberOfBooks());

        }

        /// <summary>
        /// Provide sorting function when clicking on the header of listView
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            SortAdorner.SortClick(sender, e, ref listViewSortCol, ref listViewSortAdorner, ref SearchList);
        }

        private void EditSignature_Click(object sender, RoutedEventArgs e)
        {
            ChooseSignatures window = new ChooseSignatures(true) { Owner = this };
            ChooseSignaturesModel context = window.DataContext as ChooseSignaturesModel;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowDialog();
        }

        private void Transfer_Click(object sender, RoutedEventArgs e)
        {
            TransferBook window = new TransferBook() { Owner = this };
            window.ShowDialog();
        }

        public static void CreateBarcodeImage(string number, string signature, bool isOpen)
        {
            string exportFolder = System.IO.Path.Combine(AppContext.BaseDirectory, "Barcode");
            string tempFile = System.IO.Path.Combine(exportFolder, "temp.pdf");
            string exportImage = System.IO.Path.Combine(exportFolder, String.Concat(number, ".png"));
            int imageWidth = 757;
            int imageHeight = 332;
            int textSize = 50;
            int barcodeWidth = 700;
            int barcodeHeight = 166;


            if (!Directory.Exists(exportFolder))
            {
                Directory.CreateDirectory(exportFolder);
            }

            using (PdfWriter pdfWriter = new PdfWriter(tempFile))
            {
                using (PdfDocument pdf = new PdfDocument(pdfWriter))
                {
                    Rectangle envelope = new Rectangle(imageWidth, imageHeight);
                    PageSize ps = new PageSize(envelope);
                    Document document = new Document(pdf, ps);
                    document.SetMargins(0, 0, 0, 0);
                    Paragraph text = new Paragraph(signature)
                        .SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER)
                        .SetFontSize(textSize);
                    Paragraph num = new Paragraph(number)
                        .SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER)
                        .SetFontSize(textSize);
                    Barcode128 barcode128 = new Barcode128(pdf);
                    barcode128.SetCodeType(Barcode128.CODE_C);
                    barcode128.SetCode(number);
                    barcode128.FitWidth(barcodeWidth);
                    barcode128.SetBarHeight(barcodeHeight);
                    barcode128.SetAltText("");

                    iText.Layout.Element.Image barcodeImage = new iText.Layout.Element.Image(barcode128.CreateFormXObject(pdf));
                    barcodeImage.SetHorizontalAlignment(iText.Layout.Properties.HorizontalAlignment.CENTER);
                    document.Add(text);
                    document.Add(barcodeImage);
                    document.Add(num);
                }
            }
            Spire.Pdf.PdfDocument document1 = new Spire.Pdf.PdfDocument();
            document1.LoadFromFile(tempFile);
            System.Drawing.Image img = document1.SaveAsImage(0);
            img.Save(exportImage);

            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            if (isOpen)
            {
                Process proc = new Process();
                proc.StartInfo.FileName = exportImage;
                proc.StartInfo.UseShellExecute = true;
                proc.Start();
            }
        }

        private async void CreateBarcodeCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {

            string exportImage;
            var books = SearchList.SelectedItems;
            if (books != null)
            {
                await Task.Run(() =>
                {
                    foreach (var book in books)
                    {
                        CreateBarcodeImage((book as SearchBook).Number, (book as SearchBook).Signatures, isOpen: false);
                        exportImage = System.IO.Path.Combine(ExportFolder, string.Concat((book as SearchBook).Number, ".png"));
                        if (books.Count == 1)
                        {
                            Process proc = new Process();
                            proc.StartInfo.FileName = exportImage;
                            proc.StartInfo.UseShellExecute = true;
                            proc.Start();
                        }
                        Thread.Sleep(5);
                    }
                    if (books.Count > 1)
                    {
                        Process.Start("explorer.exe", ExportFolder);
                    }
                });
            }
        }

        private void BtnBarcode_Click(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(ExportFolder))
            {
                Directory.CreateDirectory(ExportFolder);
            }
            Process.Start("explorer.exe", ExportFolder);
        }

        private async void BtnImportDB_Click(object sender, RoutedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "Sqlite Database (*.db)|*.db";
            dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (dialog.ShowDialog(this) == true)
            {
                int count = 0;
                int max = 0;
                int i = 0;
                SqliteConnection con = new SqliteConnection("" + new SqliteConnectionStringBuilder
                {
                    DataSource = dialog.FileName
                });
                con.Open();
                SqlMethods.SqlConnect(out SqliteConnection connection);

                var selectCommand = con.CreateCommand();
                selectCommand.CommandText = "SELECT COUNT(*) AS max FROM Books";
                SqliteDataReader r = selectCommand.ExecuteReader();
                r.Read();
                int.TryParse(Convert.ToString(r["max"]), out max);
                r.Close();
                selectCommand.CommandText = "SELECT b.BookId,b.Number,b.Title,b.Version,b.Medium,a.AuthorId,a.Name,s.Signature,b.Publisher,b.Place,b.Year,b.DayBought,b.Pages,b.Price " +
                    "FROM Books b " +
                    "LEFT JOIN Books_Authors ba ON (b.BookId = ba.BookId) " +
                    "LEFT JOIN Authors a ON (ba.AuthorId = a.AuthorId) " +
                    "LEFT JOIN Books_Signatures bs ON (bs.BookId = b.BookId) " +
                    "LEFT JOIN Signatures s ON (bs.SignatureId = s.SignatureId) ORDER BY b.BookId,ba.Priority,bs.Priority";
                r = selectCommand.ExecuteReader();
                int lastBookId = -1;
                int lastAuthorId = -1;
                bool finishedBook = false;
                Book tempBook = new Book();
                List<string> authors = new List<string>();
                List<string> signatures = new List<string>();
                // read  DB and forming a book object
                SearchList.IsEnabled = false;
                BoxSearchText.IsEnabled = false;
                BtnClearBookInfo.IsEnabled = false;
                BtnAddToPrint.IsEnabled = false;
                await Task.Run(() =>
                {

                    while (r.Read())
                    {
                        int result;
                        int.TryParse(Convert.ToString(r["BookId"]), out result);
                        if (result == lastBookId)
                        {
                            int result1;
                            int.TryParse(Convert.ToString(r["AuthorId"]), out result1);
                            if (result1 == lastAuthorId)
                            {
                                string temp = Convert.ToString(r["Signature"]);
                                if (!signatures.Contains(temp))
                                {
                                    signatures.Add(Convert.ToString(r["Signature"]));
                                }
                                //if (!tempBook.Signatures.Contains(temp))
                                //{
                                //    tempBook.Signatures += "-" + Convert.ToString(r["Signature"]);
                                //}
                            }
                            if (result1 != lastAuthorId)
                            {
                                authors.Add(Convert.ToString(r["Name"]));
                                //tempBook.Authors += ", " + Convert.ToString(r["Name"]);
                                lastAuthorId = result1;
                            }
                        }
                        else
                        {
                            finishedBook = true;
                            if (finishedBook && (!string.IsNullOrEmpty(tempBook.Title)))
                            {
                                authors = authors.Distinct().ToList();
                                signatures = signatures.Distinct().ToList();
                                tempBook.NoAuthor = authors.Count;
                                tempBook.Authors = new Author[tempBook.NoAuthor];

                                for (int j = 0; j < tempBook.Authors.Length; j++)
                                {
                                    tempBook.Authors[j] = new Author();
                                }
                                int k = 0;
                                foreach (var a in authors)
                                {
                                    tempBook.Authors[k++].Name = a;
                                }
                                k = 0;

                                tempBook.NoSignature = signatures.Count;
                                tempBook.Signatures = new string[tempBook.NoSignature];

                                foreach (var s in signatures)
                                {
                                    tempBook.Signatures[k++] = s;
                                }
                                var transaction = connection.BeginTransaction();
                                AddBook.AddBookToDatabase(ref connection, ref transaction, tempBook);
                                count++;
                                context.Status = "Importing";
                                context.Progress = Convert.ToInt32((double)(count) / max * 100);
                                authors.Clear();
                                signatures.Clear();
                                transaction.Commit();
                                finishedBook = false;
                            }
                            tempBook = new Book();
                            tempBook.BookId = result;
                            lastBookId = result;
                            int.TryParse(Convert.ToString(r["Number"]), out result);
                            tempBook.Number = result;
                            tempBook.Title = Convert.ToString(r["Title"]);
                            tempBook.Publisher = Convert.ToString(r["Publisher"]);
                            int.TryParse(Convert.ToString(r["Year"]), out result);
                            tempBook.Year = result;
                            int.TryParse(Convert.ToString(r["Version"]), out result);
                            tempBook.Version = result;
                            tempBook.Medium = Convert.ToString(r["Medium"]);
                            tempBook.Place = Convert.ToString(r["Place"]);
                            tempBook.DayBought = Convert.ToString(r["DayBought"]);
                            int.TryParse(Convert.ToString(r["Pages"]), out result);
                            tempBook.Pages = result;
                            double.TryParse(Convert.ToString(r["Price"]), out double result_d);
                            tempBook.Price = result_d;
                            int.TryParse(Convert.ToString(r["AuthorId"]), out result);
                            lastAuthorId = result;
                            authors.Add(Convert.ToString(r["Name"]));
                            signatures.Add(Convert.ToString(r["Signature"]));
                            //tempBook.Authors = Convert.ToString(r["Name"]);
                            //tempBook.Signatures = Convert.ToString(r["Signature"]);
                            i++;
                        }
                        Thread.Sleep(TimeSpan.FromTicks(5));
                        //progress.Report(Convert.ToInt32((double)(max - i) / max * 100));
                    }
                    if (i != 0)
                    {
                        authors = authors.Distinct().ToList();
                        signatures = signatures.Distinct().ToList();
                        tempBook.NoAuthor = authors.Count;
                        tempBook.Authors = new Author[tempBook.NoAuthor];

                        for (int j = 0; j < tempBook.Authors.Length; j++)
                        {
                            tempBook.Authors[j] = new Author();
                        }
                        int k = 0;
                        foreach (var a in authors)
                        {
                            tempBook.Authors[k++].Name = a;
                        }
                        k = 0;

                        tempBook.NoSignature = signatures.Count;
                        tempBook.Signatures = new string[tempBook.NoSignature];

                        foreach (var s in signatures)
                        {
                            tempBook.Signatures[k++] = s;
                        }
                        var transaction = connection.BeginTransaction();
                        AddBook.AddBookToDatabase(ref connection, ref transaction, tempBook);
                        count++;
                        context.Progress = Convert.ToInt32((double)(count) / max * 100);
                        context.Status = "Import Finished";
                        authors.Clear();
                        signatures.Clear();
                        transaction.Commit();
                    }
                });
                connection.Close();
                con.Close();
                Search.RunWorkerAsync(NumberOfBooks());
                SearchList.IsEnabled = true;
                BoxSearchText.IsEnabled = true;
                BtnClearBookInfo.IsEnabled = true;
                BtnAddToPrint.IsEnabled = true;
            }
        }

        private void SearchList_MouseDown(object sender, MouseButtonEventArgs e)
        {
            HitTestResult r = VisualTreeHelper.HitTest(this, e.GetPosition(this));
            if (r.VisualHit.GetType() != typeof(ListBoxItem))
            {
                SearchList.SelectedItem = null;
                ClearEntries(this.DataContext as SearchBookModel);
            }
        }

        private void GetListViewColVisible(SearchBookModel context)
        {
            string[] colHeader = { "BookId", "Number", "Signature", "Title", "Authors", "Publisher", "Year", "Version", "Medium", "Place", "Date", "Pages", "Price" };
            int[] values = new int[colHeader.Length];
            SqlMethods.SqlConnect(out SqliteConnection con);
            var selectCommand = con.CreateCommand();
            selectCommand.CommandText = "SELECT ColName, Boolean FROM ListViewColVisible";
            SqliteDataReader r = selectCommand.ExecuteReader();
            int i = 0;
            if (!r.Read())
            {
                var tr = con.BeginTransaction();
                var insertCommand = con.CreateCommand();
                insertCommand.Transaction = tr;
                foreach (var col in colHeader)
                {
                    insertCommand.CommandText = $"INSERT INTO ListViewColVisible (ColName,Boolean) VALUES ('{col}',1)";
                    insertCommand.ExecuteNonQuery();
                }
                tr.Commit();
            }
            r.Close();
            r = selectCommand.ExecuteReader();
            while (r.Read())
            {
                int result;
                if (int.TryParse(r["Boolean"].ToString(), out result))
                {
                    values[i] = result;
                    i++;
                }
            }
            con.Close();
            context.BookIdColumnVisible = values[0] == 1;
            context.NumberColumnVisible = values[1] == 1;
            context.SignaturesColumnVisible = values[2] == 1;
            context.TitleColumnVisible = values[3] == 1;
            context.AuthorsColumnVisible = values[4] == 1;
            context.PublisherColumnVisible = values[5] == 1;
            context.YearColumnVisible = values[6] == 1;
            context.VersionColumnVisible = values[7] == 1;
            context.MediumColumnVisible = values[8] == 1;
            context.PlaceColumnVisible = values[9] == 1;
            context.DateColumnVisible = values[10] == 1;
            context.PagesColumnVisible = values[11] == 1;
            context.PriceColumnVisible = values[12] == 1;
        }

        private void ContextMenuCol_Click(object sender, RoutedEventArgs e)
        {
            SearchBookModel context = this.DataContext as SearchBookModel;
            string header = (sender as MenuItem).Header.ToString();
            string colName;
            bool value;
            SqlMethods.SqlConnect(out SqliteConnection con);
            var updateCommand = con.CreateCommand();
            switch (header)
            {
                case "BookId":
                    colName = header;
                    value = context.BookIdColumnVisible;
                    break;
                case "Number":
                case "Nummer":
                    colName = "Number";
                    value = context.NumberColumnVisible;
                    break;
                case "Signatures":
                case "Signatur":
                    colName = "Signature";
                    value = context.SignaturesColumnVisible;
                    break;
                case "Book Title":
                case "Buchtitel":
                    colName = "Book Title";
                    value = context.BookIdColumnVisible;
                    break;
                case "Authors":
                case "Autor":
                    colName = "Authors";
                    value = context.AuthorsColumnVisible;
                    break;
                case "Publisher":
                case "Verlag":
                    colName = "Publisher";
                    value = context.PublisherColumnVisible;
                    break;
                case "Year":
                case "Jahr":
                    colName = "Year";
                    value = context.YearColumnVisible;
                    break;
                case "Edition":
                case "Auflage":
                    colName = "Version";
                    value = context.VersionColumnVisible;
                    break;
                case "Medium":
                    colName = header;
                    value = context.MediumColumnVisible;
                    break;
                case "Place":
                case "Standort":
                    colName = "Place";
                    value = context.PlaceColumnVisible;
                    break;
                case "Date bought":
                case "Einkauf":
                    colName = "Date";
                    value = context.DateColumnVisible;
                    break;
                case "Pages":
                case "Seiten":
                    colName = "Pages";
                    value = context.PagesColumnVisible;
                    break;
                case "Price":
                case "Preis":
                    colName = "Price";
                    value = context.PriceColumnVisible;
                    break;
                default:
                    colName = "Unknown";
                    value = true;
                    break;
            }
            int result = (value) ? 1 : 0;
            updateCommand.CommandText = $"UPDATE ListViewColVisible SET Boolean={result} WHERE ColName='{colName}'";
            updateCommand.ExecuteNonQuery();
            con.Close();
        }
    }

    public static class CustomCommands
    {
        public static readonly RoutedUICommand Exit = new RoutedUICommand("Exit", "Exit", typeof(CustomCommands), new InputGestureCollection()
        {
            new KeyGesture(Key.Q, ModifierKeys.Control)
        });
        public static readonly RoutedUICommand Undelete = new RoutedUICommand("Undelete", "Undelete", typeof(CustomCommands), new InputGestureCollection()
        {
            new KeyGesture(Key.Z, ModifierKeys.Control)
        });

        public static readonly RoutedUICommand Search = new RoutedUICommand("Search", "Search", typeof(CustomCommands));
        public static readonly RoutedUICommand Edit = new RoutedUICommand("Edit", "Edit", typeof(CustomCommands));
        public static readonly RoutedUICommand Delete = new RoutedUICommand("Delete", "Delete", typeof(CustomCommands));
        public static readonly RoutedUICommand Print = new RoutedUICommand("Print", "Print", typeof(CustomCommands));
        public static readonly RoutedUICommand AddToPrint = new RoutedUICommand("AddToPrint", "AddToPrint", typeof(CustomCommands));
        public static readonly RoutedUICommand RemoveFromPrint = new RoutedUICommand("RemoveFromPrint", "RemoveFromPrint", typeof(CustomCommands));
        public static readonly RoutedUICommand ClearPrintList = new RoutedUICommand("ClearPrintList", "ClearPrintList", typeof(CustomCommands));
        public static readonly RoutedUICommand CreateBarcode = new RoutedUICommand("CreateBarcode", "CreateBarcode", typeof(CustomCommands));

        public static readonly RoutedUICommand ClearBookInfo = new RoutedUICommand("ClearBookInfo", "ClearBookInfo", typeof(CustomCommands));

        public static readonly RoutedUICommand English = new RoutedUICommand("English", "English", typeof(CustomCommands));
        public static readonly RoutedUICommand German = new RoutedUICommand("German", "German", typeof(CustomCommands));

        public static readonly RoutedUICommand RemovePub = new RoutedUICommand("RemovePublisher", "RemovePublisher", typeof(CustomCommands));
        public static readonly RoutedUICommand SavePub = new RoutedUICommand("SavePublisher", "SavePublisher", typeof(CustomCommands));
        public static readonly RoutedUICommand RemoveMedium = new RoutedUICommand("RemoveMedium", "RemoveMedium", typeof(CustomCommands));
        public static readonly RoutedUICommand SaveMedium = new RoutedUICommand("SaveMedium", "SaveMedium", typeof(CustomCommands));
        public static readonly RoutedUICommand RemovePlace = new RoutedUICommand("RemovePlace", "RemovePlace", typeof(CustomCommands));
        public static readonly RoutedUICommand SavePlace = new RoutedUICommand("SavePlace", "SavePlace", typeof(CustomCommands));
        public static readonly RoutedUICommand RemoveSig = new RoutedUICommand("RemoveSig", "RemoveSig", typeof(CustomCommands));
        public static readonly RoutedUICommand SaveSig = new RoutedUICommand("SaveSig", "SaveSig", typeof(CustomCommands));

        public static readonly RoutedUICommand AddToTransfer = new RoutedUICommand("AddToTransfer", "AddToTransfer", typeof(CustomCommands));
        public static readonly RoutedUICommand ExportTransferList = new RoutedUICommand("ExportTransferList", "ExportTransferList", typeof(CustomCommands));
    }

    public static class CustomMessageBoxButton
    {
        public static string Yes { get => Application.Current.FindResource("MessageBox.YesBtn").ToString(); }
        public static string No { get => Application.Current.FindResource("MessageBox.NoBtn").ToString(); }
        public static string Cancel { get => Application.Current.FindResource("MessageBox.CancelBtn").ToString(); }
        public static string OK { get => Application.Current.FindResource("MessageBox.OkBtn").ToString(); }
    }
}
