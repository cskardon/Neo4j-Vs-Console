﻿namespace Anabranch.Neo4JConsolePackage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Windows;
    using System.Windows.Input;
    using GalaSoft.MvvmLight;
    using GalaSoft.MvvmLight.Command;
    using Microsoft.VisualStudio.TextManager.Interop;
    using Newtonsoft.Json;
    using RestSharp;

    public class Neo4jConsoleControlViewModel : ViewModelBase
    {
        private static class Neo4jKeyWords
        {
            private static readonly string[] Funcs = {"abs", "acos", "allShortestPaths", "asin", "atan", "atan2", "avg", "ceil", "coalesce", "collect", "cos", "cot", "count", "degrees", "e", "endnode", "exp", "extract", "filter", "floor", "haversin", "head", "id", "keys", "labels", "last", "left", "length", "log", "log10", "lower", "ltrim", "max", "min", "node", "nodes", "percentileCont", "percentileDisc", "pi", "radians", "rand", "range", "reduce", "rel", "relationship", "relationships", "replace", "right", "round", "rtrim", "shortestPath", "sign", "sin", "split", "sqrt", "startnode", "stdev", "stdevp", "str", "substring", "sum", "tail", "tan", "timestamp", "toFloat", "toInt", "trim", "type", "upper"};
            private static readonly string[] Preds = {"all", "and", "any", "has", "in", "none", "not", "or", "single", "xor"};
            private static readonly string[] Keywords = {"as", "asc", "ascending", "assert", "by", "case", "commit", "constraint", "create", "csv", "cypher", "delete", "desc", "descending", "distinct", "drop", "else", "end", "explain", "false", "fieldterminator", "foreach", "from", "headers", "in", "index", "is", "limit", "load", "match", "merge", "null", "on", "optional", "order", "periodic", "profile", "remove", "return", "scan", "set", "skip", "start", "then", "true", "union", "unique", "unwind", "using", "when", "where", "with"};

            public static IEnumerable<string> Combined(LabelsAndRelationships lar)
            {
                var combined = Funcs.Union(Preds.Union(Keywords));
                if (lar != null)
                {
                    if (lar.Labels != null)
                        combined = combined.Union(lar.Labels);
                    if (lar.Relationships != null)
                        combined = combined.Union(lar.Relationships);
                }
                return combined;
            }
        }

        public static Regex OpenBracesRegex = new Regex(
            "\\[\\s*\\[",
            RegexOptions.CultureInvariant
            | RegexOptions.Compiled
            );

        public static Regex CloseBracesRegex = new Regex(
            "\\]\\s*\\]",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static Regex OpenCloseWithCommaBracesRegex = new Regex(
            "\\]\\s*,\\s*\\[",
            RegexOptions.CultureInvariant
            | RegexOptions.Compiled
            );

        private int _currentPosition = -1;
        private string _cypherQuery;
        private string _cypherResults;
        private string _neo4jUrl;

        private IRestClient _restClient;
        private TextWrapping _resultsWrapping = TextWrapping.NoWrap;
        private IList<string> _autoCompleteItems;

        private class LabelsAndRelationships
        {
            public IEnumerable<string>  Labels { get; set; } //http://localhost:7474/db/data/labels
            public IEnumerable<string>  Relationships { get; set; } //match n-[r]->() return distinct(type(r))
        }

        private class DataHolder<T>
        {
            public T Data { get; set; }
        }

        private void SetAutoCompleteEntries()
        {
            LabelsAndRelationships lAndR = null;
            if (!string.IsNullOrWhiteSpace(Neo4jUrl))
                 lAndR = GetLabelsAndRelationships();

            _autoCompleteItems = Neo4jKeyWords.Combined(lAndR).ToList();
        }

        private LabelsAndRelationships GetLabelsAndRelationships()
        {
            var output = new LabelsAndRelationships();
            try
            {
                var res = PostUsingRestSharp("MATCH n-[r]-() RETURN DISTINCT(TYPE(r))");
                output.Relationships = JsonConvert.DeserializeObject<DataHolder<IEnumerable<string>>>(res).Data;

                var request = new RestRequest("labels");
                IRestResponse response = _restClient.Get(request);
                output.Labels = JsonConvert.DeserializeObject<IEnumerable<string>>(response.Content);
            }
            catch (Exception)
            {
                //Unable to contact the Server.
            }
            return output;
        }

        public Neo4jConsoleControlViewModel(IRestClient restClient)
        {
            CypherQuery = "MATCH (n) RETURN n LIMIT 100";
            Neo4jUrl = "http://localhost.:7474/db/data/"; //TODO: Pull from settings.
            CypherHistory = new List<string>();
            _restClient = restClient ?? new RestClient(Neo4jUrl);

            SetAutoCompleteEntries();
            SetupCommands();
        }

        public Neo4jConsoleControlViewModel() : this(null)
        {
        }

        public IList<string> CypherHistory { get; private set; }

        public ICommand NextHistoryCommand { get; set; }
        public ICommand PreviousHistoryCommand { get; set; }

        public RelayCommand<Action> PostCommand { get; set; }
        public ICommand ClearCommand { get; set; }
        public ICommand ChangeWrappingCommand { get; set; }

        public TextWrapping ResultsWrapping
        {
            get { return _resultsWrapping; }
            set
            {
                _resultsWrapping = value;
                RaisePropertyChanged("ResultsWrapping");
            }
        }

        public IEnumerable<string> AutoCompleteItems
        {
            get { return _autoCompleteItems; }
            set { _autoCompleteItems = (value == null ? new List<string>() : value.ToList() ); RaisePropertyChanged(); }
        }

        public string Neo4jUrl
        {
            get { return _neo4jUrl; }
            set
            {
                if (_neo4jUrl == value)
                    return;
                
                _neo4jUrl = value;
                if (string.IsNullOrWhiteSpace(value))
                {
                    RaisePropertyChanged();
                    return;
                }

                try
                {
                    _restClient = new RestClient(value);
                    
                }
                catch (UriFormatException)
                {
                    _restClient = null;
                    
                }

                SetAutoCompleteEntries();
                RaisePropertyChanged();
            }
        }

        

        public string CypherResults
        {
            get { return _cypherResults; }
            set
            {
                _cypherResults = value;
                RaisePropertyChanged("CypherResults");
            }
        }

        public string CypherQuery
        {
            get { return _cypherQuery; }
            set
            {
                _cypherQuery = value;
                RaisePropertyChanged("CypherQuery");
            }
        }

        private void SetupCommands()
        {
            ClearCommand = new RelayCommand(() => CypherResults = string.Empty);
            PostCommand = new RelayCommand<Action>(callback => Post(CypherQuery, callback));
            NextHistoryCommand = new RelayCommand(NextHistory);
            PreviousHistoryCommand = new RelayCommand(PreviousHistory);

            ChangeWrappingCommand = new RelayCommand(() =>
            {
                switch (ResultsWrapping)
                {
                    case TextWrapping.WrapWithOverflow:
                    case TextWrapping.Wrap:
                        ResultsWrapping = TextWrapping.NoWrap;
                        break;
                    case TextWrapping.NoWrap:
                        ResultsWrapping = TextWrapping.Wrap;
                        break;
                }
            });
        }

        #region Cypher History
        private void NextHistory()
        {
            if (CypherHistory.Count == 0)
                return;

            _currentPosition += 1;
            if (_currentPosition >= CypherHistory.Count)
                _currentPosition = CypherHistory.Count - 1;
            CypherQuery = CypherHistory[_currentPosition];
        }

        private void PreviousHistory()
        {
            if (CypherHistory.Count == 0)
                return;

            _currentPosition -= 1;
            if (_currentPosition < 0)
            {
                _currentPosition = 0;
                CypherQuery = string.Empty;
                return;
            }

            CypherQuery = CypherHistory[_currentPosition];
        }

        private void InsertHistory(string query)
        {
            if (!CypherHistory.Contains(query))
                CypherHistory.Insert(_currentPosition + 1, query);
        }
        #endregion Cypher History

        private void Post(string cypherQuery, Action callback)
        {
            if (string.IsNullOrWhiteSpace(cypherQuery) || string.IsNullOrWhiteSpace(Neo4jUrl))
                return;

            InsertHistory(cypherQuery);
            string query = cypherQuery;
            CypherQuery = string.Empty;

            DateTime start = DateTime.Now;
            string response;
            try
            {
                response = PostUsingRestSharp(query);
            }
            catch (Exception ex)
            {
                response = string.Format("Communicating to neo4j server threw a {0} with this message: {1}", ex.GetType(), ex.Message);
            }
            TimeSpan timeTaken = DateTime.Now - start;

            if (string.IsNullOrWhiteSpace(response))
                response = string.Format("No response from the server ({0}), is it running?", Neo4jUrl);
            else
            {
                try
                {
                    var ob = JsonConvert.DeserializeObject<Neo4jResponse>(response);
                    if (ob != null && ob.Data.Count > 0)
                        response = ob.ToString();

                    else
                    {
                        var err = JsonConvert.DeserializeObject<Neo4jErrorResponse>(response);
                        if (!string.IsNullOrWhiteSpace(err.Message))
                            response = err.Message;
                        else if (ob != null)
                            response = ob.ToString();
                    }
                }
                catch (Exception ex)
                {
                    response = string.Format("Couldn't deserialize ({0}) - raw data output instead:{1}", ex.Message, response);
                }
            }

            CypherResults = string.Format("{0}{1}<<<{3}>>>>{1}{2}{1}Took {4}ms{1}", CypherResults, Environment.NewLine, response, query, timeTaken.TotalMilliseconds);
            if (callback != null)
                callback();
        }

        public string PostUsingRestSharp(string data)
        {
            if (_restClient == null)
                return null;

            var request = new RestRequest("cypher");
            request.Parameters.Add(new Parameter {Name = "query", Type = ParameterType.GetOrPost, Value = data});
            IRestResponse response = _restClient.Post(request);

            return ParseContent(response.Content);
        }

        private string ParseContent(string content)
        {
            if (String.IsNullOrWhiteSpace(content))
                return null;

            content = content.Replace("\r\n", "");
            content = OpenBracesRegex.Replace(content, "[", 1);
            content = CloseBracesRegex.Replace(content, "]", 1, content.Length - 4);
            content = OpenCloseWithCommaBracesRegex.Replace(content, ",");

            return content;
        }
    }
}