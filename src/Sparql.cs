using System;
using System.Collections;
using System.IO;

using SemWeb;
using SemWeb.Stores;

using name.levering.ryan.sparql.parser;
using name.levering.ryan.sparql.parser.model;
using name.levering.ryan.sparql.model;
using name.levering.ryan.sparql.common;

namespace SemWeb.Query {

	public class Sparql : SemWeb.Query.Query {
	
		private const string BNodePersistUri = "tag:taubz.for.net,2005:bnode_persist_uri/";

		name.levering.ryan.sparql.model.Query query;
		
		public bool AllowPersistBNodes = true;
		
		public enum QueryType {
			Ask,
			Construct,
			Describe,
			Select
		}
		
		public Sparql(TextReader query)
			: this(query.ReadToEnd()) {
		}
	
		public Sparql(string query) {
			try {
				this.query = SPARQLParser.parse(new java.io.StringReader(query));
				if (this.query is SelectQuery) {
					SelectQuery sq = (SelectQuery)this.query;
					ReturnLimit = sq.getLimit();
					ReturnStart = sq.getOffset();
				}
			} catch (TokenMgrError e) {
				throw new QueryFormatException("SPARQL syntax error at: " + e.Message);
			} catch (ParseException e) {
				throw new QueryFormatException("SPARQL syntax error at: " + e.currentToken);
			}
		}
		
		public QueryType Type {
			get {
				if (query is AskQuery)
					return QueryType.Ask;
				if (query is ConstructQuery)
					return QueryType.Construct;
				if (query is DescribeQuery)
					return QueryType.Describe;
				if (query is SelectQuery)
					return QueryType.Select;
				throw new NotSupportedException("Query is of an unsupported type.");
			}
		}
	
		public void Execute(SelectableSource source, TextWriter output) {
			if (query is AskQuery)
				Ask(source, output);
			else if (query is ConstructQuery)
				Construct(source, output);
			else if (query is DescribeQuery)
				Describe(source, output);
			else if (query is SelectQuery)
				Select(source, output);
			else
				throw new NotSupportedException("Query is of an unsupported type.");
		}
	
		public bool Ask(SelectableSource source) {
			if (!(query is AskQuery))
				throw new InvalidOperationException("Only ASK queries are supported by this method (" + query.GetType() + ").");
			AskQuery q = (AskQuery)query;
			return q.execute(new RdfSourceWrapper(source, QueryMeta, this));
		}
		
		public void Ask(SelectableSource source, TextWriter output) {
			bool result = Ask(source);
			System.Xml.XmlTextWriter w = new System.Xml.XmlTextWriter(output);
			w.Formatting = System.Xml.Formatting.Indented;
			w.WriteStartElement("sparql");
			w.WriteAttributeString("xmlns", "http://www.w3.org/2001/sw/DataAccess/rf1/result");
			w.WriteStartElement("head");
			w.WriteEndElement();
			w.WriteStartElement("results");
			w.WriteStartElement("boolean");
			w.WriteString(result ? "true" : "false");
			w.WriteEndElement();
			w.WriteEndElement();
			w.WriteEndElement();
			w.Close();
		}
	
		public void Construct(SelectableSource source, StatementSink sink) {
			if (!(query is ConstructQuery))
				throw new InvalidOperationException("Only CONSTRUCT queries are supported by this method (" + query.GetType() + ").");
			ConstructQuery q = (ConstructQuery)query;
			RdfSourceWrapper sourcewrapper = new RdfSourceWrapper(source, QueryMeta, this);
			RdfGraph graph = q.execute(sourcewrapper);
			WriteGraph(graph, sourcewrapper, sink);
		}
		
		void WriteGraph(RdfGraph graph, RdfSourceWrapper sourcewrapper, StatementSink sink) {
			java.util.Iterator iter = graph.iterator();
			while (iter.hasNext()) {
				GraphStatement stmt = (GraphStatement)iter.next();
				Statement s;
				if (stmt is GraphStatementWrapper) {
					s = ((GraphStatementWrapper)stmt).s;
				} else {
					s = new Statement(
						sourcewrapper.ToEntity(stmt.getSubject()),
						sourcewrapper.ToEntity(stmt.getPredicate()),
						sourcewrapper.ToResource(stmt.getObject()),
						sourcewrapper.ToEntity(stmt.getGraphName()));
				}
				sink.Add(s);
			}
		}
		
		public void Construct(SelectableSource source, TextWriter output) {
			using (RdfWriter w = new N3Writer(output))
				Construct(source, w);
		}

		public void Describe(SelectableSource source, StatementSink sink) {
			if (!(query is DescribeQuery))
				throw new InvalidOperationException("Only DESCRIBE queries are supported by this method (" + query.GetType() + ").");
			DescribeQuery q = (DescribeQuery)query;
			RdfSourceWrapper sourcewrapper = new RdfSourceWrapper(source, QueryMeta, this);
			RdfGraph graph = q.execute(sourcewrapper);
			WriteGraph(graph, sourcewrapper, sink);
		}

		public void Describe(SelectableSource source, TextWriter output) {
			using (RdfWriter w = new N3Writer(output))
				Describe(source, w);
		}

		public void Select(SelectableSource source, TextWriter output) {
			Select(source, new SparqlXmlQuerySink(output));
		}

		public void Select(SelectableSource source, QueryResultSink sink) {
			if (!(query is SelectQuery))
				throw new InvalidOperationException("Only SELECT queries are supported by this method (" + query.GetType() + ").");
			Run(source, sink);
		}
		
		public GraphMatch ToGraphMatch() {
			if (!(query is SelectQuery))
				throw new InvalidOperationException("Only SELECT queries are supported by this method (" + query.GetType() + ").");
			
			SelectQuery sq = (SelectQuery)query;
			ASTGroupConstraint gc = sq.getConstraint() as ASTGroupConstraint;
			if (gc == null) throw new NotSupportedException();
			
			GraphMatch gm = new GraphMatch();
			Hashtable vars = new Hashtable();
			
			gm.ReturnLimit = ReturnLimit;
			gm.ReturnStart = ReturnStart;
			
			for (int i = 0; i < gc.jjtGetNumChildren(); i++) {
				ASTTripleSet triple = gc.jjtGetChild(i) as ASTTripleSet;
				if (triple == null) throw new NotSupportedException();

				Resource subj = ToGMMakeRes(triple.jjtGetChild(0), sq, gm, vars);
				if (!(subj is Entity)) continue;
				
				ASTPropertyList props = (ASTPropertyList)triple.jjtGetChild(1);
				for (int k = 0; k < props.jjtGetNumChildren(); k++) {
					ASTVerbObject vo = (ASTVerbObject)props.jjtGetChild(k);
					Resource pred = ToGMMakeRes(vo.jjtGetChild(0), sq, gm, vars);
					if (!(pred is Entity)) continue;
					
					ASTObjectList objs = (ASTObjectList)vo.jjtGetChild(1);
					for (int j = 0; j < objs.jjtGetNumChildren(); j++) {
						Resource obj = ToGMMakeRes(objs.jjtGetChild(j), sq, gm, vars);
						Statement s = new Statement((Entity)subj, (Entity)pred, obj);
						gm.AddEdge(s);
					}
				}
			}
			return gm;
		}
		Resource ToGMMakeRes(object obj, SelectQuery sq, GraphMatch gm, Hashtable vars) {
			if (obj is ASTVar) {
				ASTVar var = (ASTVar)obj;
				if (vars.ContainsKey(var.getName())) return (Resource)vars[var.getName()];
				BNode v = new BNode();
				gm.SetVariableName(v, var.getName());
				vars[var.getName()] = v;
				return v;
			} else if (obj is ASTQName) {
				ASTQName qn = (ASTQName)obj;
				qn.resolveURI(sq.getPrefixExpansions());
				return new Entity(qn.getURI());
			} else if (obj is ASTLiteral) {
				ASTLiteral lit = (ASTLiteral)obj;
				return Literal.Parse(lit.toString(), null);
			} else if (obj is ASTQuotedIRIref) {
				ASTQuotedIRIref iri = (ASTQuotedIRIref)obj;
				return new Entity(iri.getURI());
			} else {
				throw new Exception(obj.GetType().ToString());
			}
		}

		public override string GetExplanation() {
			return query.ToString();
		}
	
		public override void Run(SelectableSource source, QueryResultSink resultsink) {
			if (!(query is SelectQuery))
				throw new InvalidOperationException("Only SELECT queries are supported by this method (" + query.GetType() + ").");

			// Perform the query
			SelectQuery squery = (SelectQuery)query;
			RdfSourceWrapper sourcewrapper = new RdfSourceWrapper(source, QueryMeta, this);
			RdfBindingSet results;
			try {
				results = squery.execute(sourcewrapper);
			} catch (java.lang.Exception e) {
				throw new QueryExecutionException("Error executing query: " + e.Message, e);
			}
			
			// Prepare binding objects
			java.util.List vars = results.getVariables();
			VariableBinding[] bindings = new VariableBinding[vars.size()];
			Variable[] svars = new Variable[vars.size()];
			BNode[] vars2 = new BNode[vars.size()];
			for (int i = 0; i < bindings.Length; i++) {
				svars[i] = (Variable)vars.get(i);
				vars2[i] = new BNode();
				bindings[i] = new VariableBinding(vars2[i], svars[i].getName(), null);
			}
			
			// Initialize the result sink
			resultsink.Init(bindings);
			
			// Set the comments
			resultsink.AddComments(sourcewrapper.GetLog());

			// Iterate the bindings
			java.util.Iterator iter = results.iterator();
			long ctr = -1, ctr2 = 0;
			while (iter.hasNext()) {
				RdfBindingRow row = (RdfBindingRow)iter.next();

				// Since SPARQL processing may be lazy-delayed,
				// add any new comments that might have be logged.
				resultsink.AddComments(sourcewrapper.GetLog());
			
				ctr++;
				if (ctr < ReturnStart && ReturnStart != -1) continue;

				for (int i = 0; i < bindings.Length; i++) {
					Resource r = sourcewrapper.ToResource(row.getValue(svars[i]));
					r = sourcewrapper.Persist(r);
					bindings[i] = new VariableBinding(bindings[i].Variable, bindings[i].Name, r);
				}
				
				resultsink.Add(bindings);

				ctr2++;
				if (ctr2 >= ReturnLimit && ReturnLimit != -1) break;
			}
			
			// Close the result sink.
			resultsink.Finished();
		}
	
		class RdfSourceWrapper : RdfSource, RdfSourceWithMultiValues,
				org.openrdf.model.ValueFactory {
				
			SelectableSource source;
			Hashtable bnodes = new Hashtable();
			Entity QueryMeta;
			Sparql sparql;
			
			System.Text.StringBuilder log = new System.Text.StringBuilder();
			
			public RdfSourceWrapper(SelectableSource source, Entity meta, Sparql sparql) {
				this.source = source;
				QueryMeta = meta;
				this.sparql = sparql;
			}
			
			void Log(string message) {
				log.Append(message);
				log.Append('\n');
			}
			
			public string GetLog() {
				string ret = log.ToString();
				log.Length = 0;
				return ret;
			}
		
			private StatementIterator GetIterator(Statement statement, bool defaultGraph) {
				return GetIterator(statement.Subject == null ? null : new Entity[] { statement.Subject },
					statement.Predicate == null ? null : new Entity[] { statement.Predicate },
					statement.Object == null ? null : new Resource[] { statement.Object },
					statement.Meta == null ? null : new Entity[] { statement.Meta },
					defaultGraph);
			}
			
			private StatementIterator GetIterator(Entity[] subjects, Entity[] predicates, Resource[] objects, Entity[] metas, bool defaultGraph) {
				DateTime start = DateTime.Now;

				if (subjects == null && predicates == null && objects == null)
					throw new QueryExecutionException("Query would select all statements in the store.");
				
				if (subjects != null) Depersist(subjects);
				if (predicates != null) Depersist(predicates);
				if (objects != null) Depersist(objects);
				if (metas != null) Depersist(metas);
				
				if (subjects != null && subjects.Length > 500) subjects = null;
				if (predicates != null && predicates.Length > 500) predicates = null;
				if (objects != null && objects.Length > 500) objects = null;
				if (metas != null && metas.Length > 500) metas = null;
				
				MemoryStore results = new MemoryStore();
				StatementSink sink = results;
				
				if (!source.Distinct)
					sink = new SemWeb.Util.DistinctStatementsSink(results, defaultGraph && metas == null);

				source.Select(subjects, predicates, objects, metas, sink);
				
				Log("SELECT: " + ToString(subjects) + " " + ToString(predicates) + " " + ToString(objects) + " => " + results.StatementCount + " statements [" + (DateTime.Now-start) + "s]");
				
				return new StatementIterator(results.ToArray());
			}
			
			private string ToString(Resource[] res) {
				if (res == null) return "?";
				System.Text.StringBuilder b = new System.Text.StringBuilder();
				if (res.Length > 1) {
					b.Append("{ (");
					b.Append(res.Length);
					b.Append(") ");
				}
				foreach (Resource r in res) {
					if (b.Length > 2) b.Append(", ");
					if (b.Length > 50) { b.Append("..."); break; }
					if (r is Entity && r.Uri == null)
						b.Append("(anon)");
					if (r is Entity && r.Uri != null)
						b.Append("<" + r.Uri + ">");
					if (r is Literal)
						b.Append(r.ToString());
				}
				if (res.Length > 1) b.Append(" }");
				return b.ToString();
			}
			
		    /**
		     * Gets all the statements that come from the default graph and have a
		     * certain subject, predicate, and object. Any of the parameters can be
		     * null, in which case it assumes these are "wildcards" and all statements
		     * that match the remainding parameters will be returned.
     		 */ 
     		public java.util.Iterator getDefaultStatements (org.openrdf.model.Value subject, org.openrdf.model.URI predicate, org.openrdf.model.Value @object) {
				return GetIterator( new Statement(ToEntity(subject), ToEntity(predicate), ToResource(@object), QueryMeta), true );
			}

     		public java.util.Iterator getDefaultStatements (org.openrdf.model.Value[] subject, org.openrdf.model.Value[] predicate, org.openrdf.model.Value[] @object) {
				return GetIterator( ToEntities(subject), ToEntities(predicate), ToResources(@object), QueryMeta == null ? null : new Entity[] { QueryMeta }, true );
     		}
			
		    /**
		     * Gets all the statements that come from any graph and have a certain
		     * subject, predicate, and object. Any of the parameters can be null, in
		     * which case it assumes these are "wildcards" and all statements that match
		     * the remainding parameters will be returned.
		     * 
		     * @param the subj the subject to match statements against
		     * @param pred the predicate to match statements against
		     * @param obj the object to match statements against
		     * @return an Iterator over the matching statements
		     */
     		public java.util.Iterator getStatements (org.openrdf.model.Value subject, org.openrdf.model.URI predicate, org.openrdf.model.Value @object) {
				return GetIterator(  new Statement(ToEntity(subject), ToEntity(predicate), ToResource(@object), null), false );
			}
	
     		public java.util.Iterator getStatements (org.openrdf.model.Value[] subject, org.openrdf.model.Value[] predicate, org.openrdf.model.Value[] @object) {
				return GetIterator(  ToEntities(subject), ToEntities(predicate), ToResources(@object), null, false );
     		}
     		
		    /**
		     * Gets all the statements that come from a particular named graph and have
		     * a certain subject, predicate, and object. Any of the parameters can be
		     * null, in which case it assumes these are "wildcards" and all statements
		     * that match the remainding parameters will be returned.
		     */
     		public java.util.Iterator getStatements (org.openrdf.model.Value subject, org.openrdf.model.URI predicate, org.openrdf.model.Value @object, org.openrdf.model.URI graph) {
				return GetIterator( new Statement(ToEntity(subject), ToEntity(predicate), ToResource(@object), ToEntity(graph)), false );
			}
			
     		public java.util.Iterator getStatements (org.openrdf.model.Value[] subject, org.openrdf.model.Value[] predicate, org.openrdf.model.Value[] @object, org.openrdf.model.URI[] graph) {
				return GetIterator( ToEntities(subject), ToEntities(predicate), ToResources(@object), ToEntities(graph), false );
     		}
     		
			public org.openrdf.model.ValueFactory getValueFactory() {
				return this;
			}
			
			private bool has(Statement statement) {
				Log("CONTAINS: " + statement);
				return source.Contains(statement);
			}
			
			public bool hasDefaultStatement (org.openrdf.model.Value subject, org.openrdf.model.URI @predicate, org.openrdf.model.Value @object) {
				return has(new Statement(ToEntity(subject), ToEntity(predicate), ToResource(@object), QueryMeta));
			}
			
			public bool hasStatement (org.openrdf.model.Value subject, org.openrdf.model.URI @predicate, org.openrdf.model.Value @object) {
				return has(new Statement(ToEntity(subject), ToEntity(predicate), ToResource(@object), null));
			}
	
			public bool hasStatement (org.openrdf.model.Value subject, org.openrdf.model.URI @predicate, org.openrdf.model.Value @object, org.openrdf.model.URI graph) {
				return has(new Statement(ToEntity(subject), ToEntity(predicate), ToResource(@object), ToEntity(graph)));
			}
			
			public Entity ToEntity(org.openrdf.model.Value ent) {
				if (ent == null) return null;
				if (ent is BNodeWrapper) return ((BNodeWrapper)ent).r;
				if (ent is URIWrapper) return ((URIWrapper)ent).r;
				if (ent is org.openrdf.model.BNode) {
					org.openrdf.model.BNode bnode = (org.openrdf.model.BNode)ent;
					Entity r = (Entity)bnodes[bnode.getID()];
					if (r == null) {
						r = new BNode();
						bnodes[bnode.getID()] = r;
					}
					return r;
				} else {
					org.openrdf.model.URI uri = (org.openrdf.model.URI)ent;
					return new Entity(uri.toString());
				}
			}
			
			public Resource ToResource(org.openrdf.model.Value value) {
				if (value == null) return null;
				if (value is LiteralWrapper) return ((LiteralWrapper)value).r;
				if (value is org.openrdf.model.Literal) {
					org.openrdf.model.Literal literal = (org.openrdf.model.Literal)value;
					return new Literal(literal.getLabel(), literal.getLanguage(), literal.getDatatype() == null ? null : literal.getDatatype().toString());
				} else {
					return ToEntity(value);
				}
			}
			
			public Entity[] ToEntities(org.openrdf.model.Value[] ents) {
				if (ents == null) return null;
				ArrayList ret = new ArrayList();
				for (int i = 0; i < ents.Length; i++)
					if (!(ents[i] is org.openrdf.model.Literal))
						ret.Add( ToEntity(ents[i]) );
				return (Entity[])ret.ToArray(typeof(Entity));
			}
			public Resource[] ToResources(org.openrdf.model.Value[] ents) {
				if (ents == null) return null;
				Resource[] ret = new Resource[ents.Length];
				for (int i = 0; i < ents.Length; i++)
					ret[i] = ToResource(ents[i]);
				return ret;
			}
	
			public org.openrdf.model.BNode createBNode() {
				return new BNodeWrapper(new BNode());
			}
			public org.openrdf.model.BNode createBNode(string id) {
				throw new Exception(id);
			}
			public org.openrdf.model.Literal createLiteral(string value, string lang) {
				return new LiteralWrapper(new Literal(value, lang, null));
			}
			public org.openrdf.model.Literal createLiteral(string value, org.openrdf.model.URI datatype) {
				return new LiteralWrapper(new Literal(value, null, datatype.toString()));
			}
			public org.openrdf.model.Literal createLiteral(string value) {
				return new LiteralWrapper(new Literal(value));
			}
			public org.openrdf.model.URI createURI(string ns, string ln) {
				return new URIWrapper(new Entity(ns + ln));
			}
			public org.openrdf.model.URI createURI(string uri) {
				return new URIWrapper(new Entity(uri));
			}
			public org.openrdf.model.Statement createStatement (org.openrdf.model.Resource subject, org.openrdf.model.URI @predicate, org.openrdf.model.Value @object) {
				return new Stmt(subject, predicate, @object); 
			}
			
			class Stmt : org.openrdf.model.Statement {
				org.openrdf.model.Resource subject;
				org.openrdf.model.URI predicate;
				org.openrdf.model.Value @object;
				public Stmt(org.openrdf.model.Resource subject, org.openrdf.model.URI @predicate, org.openrdf.model.Value @object) {
					this.subject = subject;
					this.predicate = predicate;
					this.@object = @object;
				}
				public org.openrdf.model.Resource getSubject() { return subject; }
				public org.openrdf.model.URI getPredicate() { return predicate; }
				public org.openrdf.model.Value getObject() { return @object; }
			}
			
			public void Depersist(Resource[] r) {
				for (int i = 0; i < r.Length; i++)
					r[i] = Depersist(r[i]);
			}
			
			public Resource Depersist(Resource r) {
				if (r.Uri == null || !sparql.AllowPersistBNodes) return r;
				if (!(source is SupportsPersistableBNodes)) return r;
				if (!r.Uri.StartsWith(Sparql.BNodePersistUri)) return r;
				
				SupportsPersistableBNodes spb = (SupportsPersistableBNodes)source;
				string uri = r.Uri;
				string guid = uri.Substring(Sparql.BNodePersistUri.Length);
				int c = guid.IndexOf(':');
				if (c > 0) {
					string id = guid.Substring(c+1);
					guid = guid.Substring(0, c);
					if (spb.GetStoreGuid() != null && guid == spb.GetStoreGuid()) {
						BNode node = spb.GetNodeFromId(id);
						if (node != null)
							return node;
					}
				}
				
				return r;
			}
			
			public Resource Persist(Resource r) {
				if (!(r is BNode) || !sparql.AllowPersistBNodes) return r;
				if (!(source is SupportsPersistableBNodes)) return r;
				SupportsPersistableBNodes spb = (SupportsPersistableBNodes)source;
				string guid = spb.GetStoreGuid();
				if (guid == null) return r;
				string id = spb.GetNodeId((BNode)r);
				if (id == null) return r;
				return new Entity(Sparql.BNodePersistUri + guid + ":" + id);
			}
		}
		
		class StatementIterator : java.util.Iterator {
			Statement[] statements;
			int curindex = -1;
			
			public StatementIterator(Statement[] statements) {
				this.statements = statements;
			}
			
			public bool hasNext() {
				return curindex + 1 < statements.Length;
			}
			
			public object next() {
				curindex++;
				return new GraphStatementWrapper(statements[curindex]);
			}
			
			public void remove() {
			}
		}
		
		class GraphStatementWrapper : GraphStatement {
			public readonly Statement s;
			public GraphStatementWrapper(Statement statement) {
				s = statement;
			}
			
			public org.openrdf.model.URI getGraphName() {
				return new URIWrapper(s.Meta);
			}
			
			public org.openrdf.model.Value getSubject() {
				if (s.Subject.Uri == null)
					return new BNodeWrapper((BNode)s.Subject);
				else
					return new URIWrapper(s.Subject);
			}
	
			public org.openrdf.model.URI getPredicate() {
				if (s.Predicate.Uri == null)
					throw new QueryExecutionException("Statement's predicate is a blank node.");
				return new URIWrapper(s.Predicate);
			}
	
			public org.openrdf.model.Value getObject() {
				if (s.Object is Literal)
					return new LiteralWrapper((Literal)s.Object);
				else if (s.Object.Uri == null)
					return new BNodeWrapper((BNode)s.Object);
				else
					return new URIWrapper((Entity)s.Object);
			}
		}
		
		class BNodeWrapper : java.lang.Object, org.openrdf.model.BNode {
			public BNode r;
			public BNodeWrapper(BNode res) { r = res; }
			public string getID() { throw new NotSupportedException(); }
			public override bool equals(object other) {
				if (!(other is BNodeWrapper)) return false;
				return r.Equals(((BNodeWrapper)other).r);
			}
			public override int hashCode() { return r.GetHashCode(); }
		}
	
		class URIWrapper : java.lang.Object, org.openrdf.model.URI {
			public Entity r;
			public URIWrapper(Entity res) { r = res; }
			public string getLocalName() { return ""; }
			public string getNamespace() { return r.Uri; }
			string org.openrdf.model.URI.toString() { return r.Uri; }
			public override string toString() { return r.Uri; }
			public override bool equals(object other) {
				if (other is URIWrapper)
					return r.Equals(((URIWrapper)other).r);
				else if (other is org.openrdf.model.URI)
					return r.Uri == ((org.openrdf.model.URI)other).toString();
				else
					return false;
			}
			public override int hashCode() { return java.lang.String.instancehelper_hashCode(r.Uri); }
		}
	
		class LiteralWrapper : java.lang.Object, org.openrdf.model.Literal {
			public Literal r;
			public LiteralWrapper(Literal res) { r = res; }
			public org.openrdf.model.URI getDatatype() { if (r.DataType == null) return null; return new URIWrapper(r.DataType); }
			public string getLabel() { return r.Value; }
			public string getLanguage() { return r.Language; }
			public override bool equals(object other) {
				if (other is LiteralWrapper)
					return r.Equals(((LiteralWrapper)other).r);
				else if (other is org.openrdf.model.Literal)
					return r.Equals(GetLiteral((org.openrdf.model.Literal)other));
				return false;
			}
			public override int hashCode() { return java.lang.String.instancehelper_hashCode(r.Value); }
			static Literal GetLiteral(org.openrdf.model.Literal literal) {
				return new Literal(literal.getLabel(), literal.getLanguage(),
					literal.getDatatype() == null ? null
						: literal.getDatatype().toString());
			}
		}
	}
	
	public class SparqlProtocol : System.Web.IHttpHandler {
		public int MaximumLimit = -1;
		public string MimeType = "application/sparql-results+xml";
		
		Hashtable sources = new Hashtable();
	
		bool System.Web.IHttpHandler.IsReusable { get { return true; } }
		
		public virtual void ProcessRequest(System.Web.HttpContext context) {
			try {
				string query = context.Request["query"];
				if (query == null || query.Trim() == "")
					throw new QueryFormatException("No query provided.");
				
				// Buffer the response so that any errors while
				// executing don't get outputted after the response
				// has begun.
				
				MemoryStream buffer = new MemoryStream();

				bool closeAfterQuery;
				SelectableSource source = GetDataSource(out closeAfterQuery);
				try {
					Query sparql = CreateQuery(query);
					RunQuery(sparql, source, new StreamWriter(buffer));
				} finally {
					if (closeAfterQuery && source is IDisposable)
						((IDisposable)source).Dispose();
				}
				
				if (context.Request["outputMimeType"] == null)
					context.Response.ContentType = MimeType;
				else
					context.Response.ContentType = context.Request["outputMimeType"];

				context.Response.OutputStream.Write(buffer.GetBuffer(), 0, (int)buffer.Length);
				
			} catch (QueryFormatException e) {
				context.Response.ContentType = "text/plain";
				context.Response.StatusCode = 400;
				context.Response.StatusDescription = e.Message;
				context.Response.Write(e.Message);
			} catch (QueryExecutionException e) {
				context.Response.ContentType = "text/plain";
				context.Response.StatusCode = 500;
				context.Response.StatusDescription = e.Message;
				context.Response.Write(e.Message);
			}
		}

		protected virtual SelectableSource GetDataSource(out bool closeAfterQuery) {
			closeAfterQuery = false;
			
			if (System.Web.HttpContext.Current == null)
				throw new InvalidOperationException("This method is not valid outside of an ASP.NET request.");

			string path = System.Web.HttpContext.Current.Request.Path;
			lock (sources) {
				SelectableSource source = (SelectableSource)sources[path];
				if (source != null) return source;

				System.Collections.Specialized.NameValueCollection config = (System.Collections.Specialized.NameValueCollection)System.Configuration.ConfigurationSettings.GetConfig("sparqlSources");
				if (config == null)
					throw new InvalidOperationException("No sparqlSources config section is set up.");

				string spec = config[path];
				if (spec == null)
					throw new InvalidOperationException("No data source is set for the path " + path + ".");
					
				bool reuse = true;
				if (spec.StartsWith("noreuse,")) {
					reuse = false;
					closeAfterQuery = true;
					spec = spec.Substring("noreuse,".Length);
				}

				StatementSource src = Store.CreateForInput(spec);
				if (!(src is SelectableSource))
					src = new MemoryStore(src);
					
				if (reuse)
					sources[path] = src;

				return (SelectableSource)src;
			}
		}
		
		protected virtual Query CreateQuery(string query) {
			Query sparql = new Sparql(query);
			if (MaximumLimit != -1 && (sparql.ReturnLimit == -1 || sparql.ReturnLimit > MaximumLimit)) sparql.ReturnLimit = MaximumLimit;
			return sparql;
		}
		
		protected virtual void RunQuery(Query query, SelectableSource source, TextWriter output) {
			query.Run(source, new SparqlXmlQuerySink(output));
		}
	}
}
