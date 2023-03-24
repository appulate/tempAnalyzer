using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;

namespace myAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ConstructorArgumentsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ConstructorArguments";
    private const string Title = "Constructor argument formatting";

    private const string MessageFormat =
        "Constructor should have 2 arguments in line, if has 3 or more arguments each argument should be on a new line.";

    private const string Category = "Formatting";
    private const string Description = "Enforces constructor argument formatting.";

    private static readonly DiagnosticDescriptor Rule = new (DiagnosticId, Title, MessageFormat,
                                                             Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeConstructorDeclaration, SyntaxKind.ConstructorDeclaration);
        
    }

    private static void AnalyzeConstructorDeclaration(SyntaxNodeAnalysisContext context)
    {
        var constructor = (ConstructorDeclarationSyntax)context.Node;
        var arguments = constructor.ParameterList.Parameters;
        var argumentCount = arguments.Count;
        
        if (argumentCount < 3)
        {
            return;
        }

        // Three or more arguments should be on separate lines.
        var previousEndLine = arguments[0].GetLocation().GetLineSpan().EndLinePosition.Line;

        for (int i = 1; i < argumentCount; i++)
        {
            var currentEndLine = arguments[i].GetLocation().GetLineSpan().EndLinePosition.Line;

            if (previousEndLine == currentEndLine)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, arguments[i].GetLocation()));
                break;
            }

            previousEndLine = currentEndLine;
        }
    }
}

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConstructorArgumentsCodeFixProvider)), Shared]
public class ConstructorArgumentsCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ConstructorArgumentsAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var constructor = root.FindToken(diagnosticSpan.Start).Parent.AncestorsAndSelf().OfType<ConstructorDeclarationSyntax>().First();

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Fix constructor argument formatting",
                createChangedDocument: cancellationToken => FixConstructorArgumentFormattingAsync(context.Document, constructor, cancellationToken),
                equivalenceKey: "Fix constructor argument formatting"),
            diagnostic);
    }

    private static async Task<Document> FixConstructorArgumentFormattingAsync(Document document, ConstructorDeclarationSyntax constructor, CancellationToken cancellationToken)
    {
        var newArguments = new List<SyntaxNode>();
        var arguments = constructor.ParameterList.Parameters;

        // Add first argument to new argument list
        newArguments.Add(arguments[0]);
        var previousEndLine = arguments[0].GetLocation().GetLineSpan().EndLinePosition.Line;

        for (int i = 1; i < arguments.Count; i++)
        {
            var currentEndLine = arguments[i].GetLocation().GetLineSpan().EndLinePosition.Line;

            if (previousEndLine == currentEndLine)
            {
                var parameterSyntax = arguments[i];
                parameterSyntax = parameterSyntax.WithLeadingTrivia(
                    SyntaxFactory.SyntaxTrivia(SyntaxKind.EndOfLineTrivia, Environment.NewLine));
                newArguments.Add(parameterSyntax);
            }
            else
            {
                // Add current argument to new argument list
                newArguments.Add(arguments[i]);
            }

            previousEndLine = currentEndLine;
        }



        SyntaxNode newConstructor = constructor
            .WithParameterList(constructor.ParameterList.WithParameters(SyntaxFactory.SeparatedList(newArguments)))
            .WithAdditionalAnnotations(Formatter.Annotation);
        
        newConstructor = Formatter.Format(newConstructor,document.Project.Solution.Workspace);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var newRoot = root.ReplaceNode(constructor, newConstructor);
        return document.WithSyntaxRoot(newRoot);
       
    }
}
