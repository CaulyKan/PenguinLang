grammar PenguinLang;

primaryExpression:
	Constant
	| StringLiteral+
	| boolLiteral
	| voidLiteral
	| identifierWithGeneric
	| lambdaFunctionExpression
	| '(' expression ')'
	| codeBlockExpression
	| ifExpression
	| whileExpression
	| 'cast' '<' typeSpecifier '>' '(' expression ')';

// if as expression - returns the value of the executed branch
ifExpression:
	'if' '(' expression ')' codeBlockExpression (
		'else' (codeBlockExpression | ifExpression)
	)?;

// while as expression - value comes from break statements
whileExpression: 'while' '(' expression ')' codeBlockExpression;

codeBlockExpression: '{' codeBlockItem* expression? '}';

identifierWithGeneric: identifier genericArguments?;

postfixExpression:
	primaryExpression
	| postfixExpression '.' identifierWithGeneric
	| postfixExpression '(' expression? (',' expression)* ')'
	| 'new' typeSpecifier '(' expression? (',' expression)* ')'
	| 'async' expression
	| 'wait' expression?;

unaryExpression:
	postfixExpression
	| unaryOperator postfixExpression;

unaryOperator: '&' | '*' | '+' | '-' | '~' | '!';

multiplicativeExpression:
	unaryExpression (multiplicativeOperator unaryExpression)*;

multiplicativeOperator: '*' | '/' | '%';

additiveExpression:
	multiplicativeExpression (
		additiveOperator multiplicativeExpression
	)*;

additiveOperator: '+' | '-';

relationalExpression:
	additiveExpression (relationalOperator additiveExpression)*;

relationalOperator: '<' | '>' | '<=' | '>=';

equalityExpression:
	relationalExpression (equalityOperator relationalExpression)?;

equalityOperator: '==' | '!=' | 'is';

bitwiseAndExpression:
	equalityExpression ('&' equalityExpression)*;

bitwiseXorExpression:
	bitwiseAndExpression ('^' bitwiseAndExpression)*;

bitwiseOrExpression:
	bitwiseXorExpression ('|' bitwiseXorExpression)*;

logicalAndExpression:
	bitwiseOrExpression ('&&' bitwiseOrExpression)*;

logicalOrExpression:
	logicalAndExpression ('||' logicalAndExpression)*;

expression: logicalOrExpression;

assignmentOperator:
	'='
	| '*='
	| '/='
	| '%='
	| '+='
	| '-='
	| '&='
	| '^='
	| '|=';

typeReferenceDeclaration: 'type' identifier '=' typeSpecifier;

declarationWithoutInitializer: identifier ':' typeSpecifier;

declaration: identifier (':' typeSpecifier)? ( '=' expression)?;

classDeclaration:
	identifier (':' typeMutabilitySpecifier? typeSpecifier)? (
		'=' expression
	)?;

letKeyword: 'let' 'mut'?;

storageClassSpecifier: 'extern';

typeMutabilitySpecifier: 'mut' | '!mut' | 'auto';

typeSpecifier:
	typeMutabilitySpecifier? typeSpecifierWithoutIterable iterableType?;

typeSpecifierWithoutIterable:
	'void'
	| 'u8'
	| 'u16'
	| 'u32'
	| 'u64'
	| 'i8'
	| 'i16'
	| 'i32'
	| 'i64'
	| 'float'
	| 'double'
	| 'string'
	| 'bool'
	| 'char'
	| 'Self'
	| (('fun' | 'async_fun') genericArguments)
	| (
		identifierWithDotsAndGenericArguments (
			'.' identifierWithDotsAndGenericArguments
		)*
	);

iterableType: '[]';

identifierWithDots: identifier ('.' identifier)*;

identifierWithDotsAndGenericArguments:
	identifierWithDots genericArguments?;

genericArguments:
	'<' typeSpecifier (',' typeSpecifier)* (
		',' variadicGenericArguments
	)? '>';

variadicGenericArguments: '...';

// Template declaration for type/constant parameters on definitions
templateParameter: identifier ':' ('type' | typeSpecifier);

templateDeclaration:
	'#' 'template' '(' templateParameter (',' templateParameter)* ')';

whereClause: identifier ':' typeSpecifier;

whereDefinition: 'where' (whereClause (',' whereClause)*);

interfaceDefinition:
	(templateDeclaration)? 'interface' identifier '{' (
		(declaration ';')
		| functionDefinition
		| interfaceImplementation
		| eventDefinition
	)* '}';

interfaceImplementation:
	'impl' typeSpecifier (whereDefinition)? (
		';'
		| ( '{' (functionDefinition)* '}')
	);

interfaceForImplementation:
	'impl' typeSpecifier 'for' typeSpecifier (whereDefinition)? (
		';'
		| ( '{' (functionDefinition)* '}')
	);

classDefinition:
	(templateDeclaration)? 'class' identifier '{' (
		(classDeclaration ';')
		| functionDefinition
		| interfaceImplementation
		| eventDefinition
		| onRoutine
	)* '}';

enumDefinition:
	(templateDeclaration)? 'enum' identifier '{' (
		enumDeclaration
		| functionDefinition
		| interfaceImplementation
		| eventDefinition
		| onRoutine
	)* '}';

enumDeclaration: identifier (':' typeSpecifier)? ';';

nestedParenthesesBlock: (
		~('(' | ')')
		| '(' nestedParenthesesBlock ')'
	)*;

//typeName: specifierQualifierList identifier?;

codeBlockItem:
	statement
	| (letKeyword declaration ';')
	| (typeReferenceDeclaration ';');

statement:
	codeBlockExpression
	| expressionStatement
	| ifStatement
	| whileStatement
	| forStatement
	| assignmentStatement
	| jumpStatement
	| returnStatement
	| yieldStatement
	| signalStatement
	| emitEventStatement
	| ';';

assignmentStatement:
	postfixExpression assignmentOperator expression ';';

expressionStatement: expression ';';

ifStatement:
	'if' '(' expression ')' statement ('else' statement)?;
//| 'switch' '(' expression ')' statement;

whileStatement: 'while' '(' expression ')' statement;

forStatement:
	'for' '(' letKeyword declaration 'in' expression ')' statement;

jumpStatement: 'continue' ';' | 'break' expression? ';';

returnStatement: returnKeyword expression? ';';

returnKeyword:
	'return'
	| '__yield_not_finished_return'
	| '__yield_finished_return'
	| '__blocked_return';

yieldStatement: 'yield' expression? ';';

signalStatement: '__signal' expression;

emitEventStatement: 'emit' expression '(' expression? ')' ';';

compilationUnit: namespaceDeclaration* EOF;

namespaceDeclaration:
	(letKeyword declaration ';')
	| typeReferenceDeclaration
	| namespaceDefinition
	| initialRoutine
	| functionDefinition
	| classDefinition
	| enumDefinition
	| interfaceDefinition
	| interfaceForImplementation
	| eventDefinition
	| onRoutine
	| ';';

thisParameter: 'mut'? 'this';
parameterList: (thisParameter | declaration)? (',' declaration)* ','?;

functionSpecifier:
	'pure'
	| '!pure'
	| 'extern'
	| 'async'
	| '!async';

lambdaFunctionExpression:
	('fun' | 'async_fun') ('(' parameterList ')')? (
		'->' typeSpecifier
	)? codeBlockExpression;

functionDefinition:
	functionSpecifier* 'fun' (identifier | 'new') (
		'(' parameterList ')'
	)? ('->' typeSpecifier)? (codeBlockExpression | ';');

eventDefinition: 'event' identifier (':' typeSpecifier)? ';';

initialRoutine: 'initial' identifier? codeBlockExpression;

onRoutine:
	'on' expression ('(' declarationWithoutInitializer? ')')? codeBlockExpression;

namespaceDefinition:
	'namespace' identifier '{' namespaceDeclaration* '}';

identifier: Identifier | 'this';

Identifier: IdentifierNondigit (IdentifierNondigit | Digit)*;

fragment IdentifierNondigit: Nondigit;
//| UniversalCharacterName | // other implementation-defined characters...;

fragment Nondigit: [a-zA-Z_];

fragment Digit: [0-9];

fragment UniversalCharacterName:
	'\\u' HexQuad
	| '\\U' HexQuad HexQuad;

fragment HexQuad:
	HexadecimalDigit HexadecimalDigit HexadecimalDigit HexadecimalDigit;

Constant:
	IntegerConstant
	| FloatingConstant
	//|   EnumerationConstant
	| CharacterConstant;

fragment IntegerConstant:
	DecimalConstant IntegerSuffix?
	| OctalConstant IntegerSuffix?
	| HexadecimalConstant IntegerSuffix?
	| BinaryConstant;

fragment BinaryConstant: '0' [bB] [0-1]+;

fragment DecimalConstant: NonzeroDigit Digit*;

fragment OctalConstant: '0' OctalDigit*;

fragment HexadecimalConstant:
	HexadecimalPrefix HexadecimalDigit+;

fragment HexadecimalPrefix: '0' [xX];

fragment NonzeroDigit: [1-9];

fragment OctalDigit: [0-7];

fragment HexadecimalDigit: [0-9a-fA-F];

fragment IntegerSuffix:
	UnsignedSuffix LongSuffix?
	| UnsignedSuffix LongLongSuffix
	| LongSuffix UnsignedSuffix?
	| LongLongSuffix UnsignedSuffix?;

fragment UnsignedSuffix: [uU];

fragment LongSuffix: [lL];

fragment LongLongSuffix: 'll' | 'LL';

fragment FloatingConstant:
	DecimalFloatingConstant
	| HexadecimalFloatingConstant;

fragment DecimalFloatingConstant:
	FractionalConstant ExponentPart? FloatingSuffix?
	| DigitSequence ExponentPart FloatingSuffix?;

fragment HexadecimalFloatingConstant:
	HexadecimalPrefix (
		HexadecimalFractionalConstant
		| HexadecimalDigitSequence
	) BinaryExponentPart FloatingSuffix?;

fragment FractionalConstant:
	DigitSequence? '.' DigitSequence
	| DigitSequence '.';

fragment ExponentPart: [eE] Sign? DigitSequence;

fragment Sign: [+-];

DigitSequence: Digit+;

fragment HexadecimalFractionalConstant:
	HexadecimalDigitSequence? '.' HexadecimalDigitSequence
	| HexadecimalDigitSequence '.';

fragment BinaryExponentPart: [pP] Sign? DigitSequence;

fragment HexadecimalDigitSequence: HexadecimalDigit+;

fragment FloatingSuffix: [flFL];

fragment CharacterConstant:
	'\'' CCharSequence '\''
	| 'L\'' CCharSequence '\''
	| 'u\'' CCharSequence '\''
	| 'U\'' CCharSequence '\'';

fragment CCharSequence: CChar+;

fragment CChar: ~['\\\r\n] | EscapeSequence;

fragment EscapeSequence:
	SimpleEscapeSequence
	| OctalEscapeSequence
	| HexadecimalEscapeSequence
	| UniversalCharacterName;

fragment SimpleEscapeSequence: '\\' ['"?abfnrtv\\];

fragment OctalEscapeSequence:
	'\\' OctalDigit OctalDigit? OctalDigit?;

fragment HexadecimalEscapeSequence: '\\x' HexadecimalDigit+;

StringLiteral: EncodingPrefix? '"' SCharSequence? '"';

boolLiteral: 'true' | 'false';

voidLiteral: 'void';

fragment EncodingPrefix: 'u8' | 'u' | 'U' | 'L';

fragment SCharSequence: SChar+;

fragment SChar:
	~["\\\r\n]
	| EscapeSequence
	| '\\\n' // Added line
	| '\\\r\n'; // Added line

Whitespace: [ \t]+ -> channel(HIDDEN);

Newline: ('\r' '\n'? | '\n') -> channel(HIDDEN);

BlockComment: '/*' .*? '*/' -> channel(HIDDEN);

LineComment: '//' ~[\r\n]* -> channel(HIDDEN);