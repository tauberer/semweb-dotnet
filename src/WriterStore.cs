using System;
using System.Collections;

using SemWeb;

namespace SemWeb.Stores {
	public class WriterStore : Store, IDisposable {
		RdfWriter writer;
		long anonId = 0;
		
		int ctr = 0;
		
		Entity currentMeta;
		
		public WriterStore(RdfWriter writer, KnowledgeModel model) : base(model) { this.writer = writer; }
		
		public override int StatementCount { get { return ctr; } }
		
		public override void Clear() {
			throw new InvalidOperationException();
		}
		
		public override void Import(RdfParser parser) {
			base.Import(parser);
			if (currentMeta != null)
				writer.PopMetaScope();
			writer.Close();
		}
		
		public void Dispose() {
			writer.Dispose();
		}
		
		private string GetURI(Entity entity) {
			object key = GetResourceKey(entity);
			if (key != null)
				return (string)key;
			if (entity.Uri == null) throw new ArgumentException("Cannot add a statement with an anonymous resource not created from this store.");
			return entity.Uri;
		}
		
		public override void Add(Statement statement) {
			ctr++;
			
			if (statement.Meta != currentMeta) {
				if (currentMeta != null)
					writer.PopMetaScope();
				if (statement.Meta != null)
					writer.PushMetaScope(GetURI(statement.Meta));
				currentMeta = statement.Meta;
			}
			
			string subj = GetURI(statement.Subject);
			string pred = GetURI(statement.Predicate);

			if (statement.Object is Literal) {
				Literal lit = (Literal)statement.Object;
				writer.WriteStatement(subj, pred, lit);
			} else {
				writer.WriteStatement(subj, pred, GetURI((Entity)statement.Object));
			}
		}
		
		public override Entity[] GetAllEntities() {
			throw new InvalidOperationException();
		}
		
		public override Entity[] GetAllPredicates() {
			throw new InvalidOperationException();
		}
			
		public override bool Contains(Statement statement) {
			throw new InvalidOperationException();
		}
		
		public override void Remove(Statement statement) {
			throw new InvalidOperationException();
		}
		
		public override void Select(Statement template, SelectPartialFilter partialFilter, StatementSink result) {
			throw new InvalidOperationException();
		}
		
		public override void Select(Statement[] templates, SelectPartialFilter partialFilter, StatementSink result) {
			throw new InvalidOperationException();
		}

		public override Entity GetResource(string uri, bool create) {
			return new Entity(uri, Model);
		}
		
		public override Entity CreateAnonymousResource() {
			Entity e = new Entity(Model);
			SetResourceKey(e, writer.CreateAnonymousNode());
			return e;
		}

		public override void Replace(Entity a, Entity b) {
			throw new InvalidOperationException();
		}
	}
	
}
